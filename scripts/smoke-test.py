"""Integration acceptance test against a RUNNING, disposable local stack.
Requires requests and Pillow. Creates two synthetic variants, tests both face assignments,
then deletes test records. No assertion of accuracy on real coasters.
Run: python scripts/smoke-test.py --url http://localhost:8080
APP_PASSWORD can be provided through the environment or the root .env file.
"""
import argparse
import io
import os
import random
from pathlib import Path
import requests
from PIL import Image, ImageDraw

parser=argparse.ArgumentParser();parser.add_argument('--url',default='http://localhost:8080');args=parser.parse_args()
secret=os.getenv('APP_PASSWORD')
if not secret:
    env=Path(__file__).resolve().parents[1]/'.env'
    if env.exists():
        secret=next((line.split('=',1)[1] for line in env.read_text().splitlines() if line.startswith('APP_PASSWORD=')),None)
if not secret:raise SystemExit('Set APP_PASSWORD or generate .env first.')
session=requests.Session();session.headers['X-Coaster-Request']='1'
base=args.url.rstrip('/')+'/api'
r=session.post(base+'/login',json={'password':secret},timeout=20);r.raise_for_status()

def photo(seed):
    rng=random.Random(seed);im=Image.new('RGB',(650,650),'#eee6cc');d=ImageDraw.Draw(im)
    d.ellipse((20,20,630,630),fill=tuple(rng.randint(0,150) for _ in range(3)),outline='black',width=12)
    for i in range(90):
        x,y=rng.randint(100,500),rng.randint(100,500);d.rectangle((x,y,x+20,y+25),fill=tuple(rng.randint(0,255) for _ in range(3)))
    d.text((220,310),f'TEST COASTER {seed}',fill='white',stroke_width=2)
    out=io.BytesIO();im.save(out,'JPEG');return out.getvalue()
def scan(f,b):
    r=session.post(base+'/search',files={'front':('front.jpg',f,'image/jpeg'),'back':('back.jpg',b,'image/jpeg')},timeout=600);r.raise_for_status();return r.json()
ids=[]
try:
    # Reject invalid uploads and unauthenticated access.
    assert requests.get(base+'/coasters',timeout=10).status_code==401
    assert session.post(base+'/search',files={'front':('x',b'bad'),'back':('y',b'bad')},timeout=30).status_code==400
    for seed in (110,230):
        result=scan(photo(seed),photo(seed+1))
        r=session.post(base+'/coasters',json={'scanId':result['scanId'],'coaster':{'title':f'INTEGRATION TEST {seed}'},'copy':{'binder':'TEST','page':'1','position':'1'}},timeout=30);r.raise_for_status();ids.append(r.json()['id'])
        # A consumed scan must not create another entry.
        assert session.post(base+'/coasters',json={'scanId':result['scanId'],'coaster':{'title':'REPLAY'},'copy':{}},timeout=30).status_code==400
    same=scan(photo(110),photo(111));assert same['candidates'][0]['coaster']['id']==ids[0]
    swapped=scan(photo(111),photo(110));assert swapped['candidates'][0]['coaster']['id']==ids[0];assert swapped['candidates'][0]['swapped']
    r=session.post(base+f'/coasters/{ids[0]}/copies',json={'binder':'TEST','page':'2'},timeout=20);r.raise_for_status()
    r=session.get(base+'/coasters',timeout=20);r.raise_for_status();item=next(x for x in r.json() if x['id']==ids[0]);assert len(item['copies'])==2
    for side in ('front','back'):assert session.get(base+f'/coasters/{ids[0]}/{side}',timeout=20).headers['Content-Type'].startswith('image/jpeg')
    print('PASS: authentication, invalid uploads, save, replay protection, exact and swapped match, copies, images.')
finally:
    for id in ids:
        session.delete(base+'/coasters/'+id,timeout=20).raise_for_status()
