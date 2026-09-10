from io import BytesIO
from pathlib import Path
import numpy as np
import pytest
from PIL import Image, ImageDraw
from imaging import normalize, safe_path, geometric_similarity, pair_detail

def encode(im):
    out = BytesIO(); im.save(out, format="PNG"); return out.getvalue()

def test_blank_and_limits():
    im, warnings, blank = normalize(encode(Image.new("RGB", (600, 600), "white")))
    assert blank and warnings and im.mode == "RGB"
    with pytest.raises(ValueError): normalize(b"not an image")
    with pytest.raises(ValueError): normalize(encode(Image.new("RGB", (30, 30))))

def test_alpha_and_downscale():
    im, _, _ = normalize(encode(Image.new("RGBA", (2200, 1800), (0, 0, 0, 0))))
    assert max(im.size) == 1400 and im.getpixel((0, 0)) == (255, 255, 255)

def test_path_traversal(tmp_path):
    assert safe_path(tmp_path, "a/front.jpg") == tmp_path / "a/front.jpg"
    with pytest.raises(ValueError): safe_path(tmp_path, "../secret.jpg")
    with pytest.raises(ValueError): safe_path(tmp_path, "/etc/passwd")

def test_rotation_matches_and_unrelated_does_not(tmp_path):
    rng = np.random.default_rng(42)
    im = Image.new("RGB", (600, 600), "white"); draw = ImageDraw.Draw(im)
    for _ in range(120):
        x, y = rng.integers(20, 550, 2).tolist()
        draw.rectangle((x,y,x+15,y+20), fill=tuple(rng.integers(0,180,3).tolist()))
    a=tmp_path/'a.jpg'; b=tmp_path/'b.jpg'; c=tmp_path/'c.jpg'
    im.save(a); im.rotate(90).save(b)
    Image.fromarray(rng.integers(0,255,(600,600,3),dtype=np.uint8)).save(c)
    assert geometric_similarity(a,b) > 0.6
    assert geometric_similarity(a,c) < 0.2

def test_pair_requires_both_sides():
    assert pair_detail([1.0, 0.0]) == 0.35
    assert pair_detail([None, 0.8]) == 0.8
    assert pair_detail([None, None]) is None
