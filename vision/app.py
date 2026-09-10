import base64
import os
from contextlib import asynccontextmanager
from pathlib import Path
from threading import Lock
import numpy as np
from PIL import Image, ImageOps
from fastapi import FastAPI, File, HTTPException, UploadFile
from pydantic import BaseModel, Field, ConfigDict
from imaging import normalize, jpeg_bytes, safe_path, geometric_similarity, pair_detail

MODEL_ID = "facebook/dinov2-small"
# Pinned model revision filled from the model repository when packaging.
MODEL_REVISION = "ed25f3a31f01632728cabb09d1542f84ab7b0056"
MODEL_VERSION = f"{MODEL_ID}@{MODEL_REVISION}:rgb-pad224-rot4-mean-v1"
ROOT = Path(os.getenv("MEDIA_ROOT", "/media"))
lock = Lock()
model = None

@asynccontextmanager
async def lifespan(app):
    global model
    import torch
    from transformers import AutoModel
    torch.set_num_threads(int(os.getenv("OMP_NUM_THREADS", "2")))
    print("Loading DINOv2: the first start downloads model weights (~90 MB).", flush=True)
    model = AutoModel.from_pretrained(MODEL_ID, revision=MODEL_REVISION, use_safetensors=True).eval()
    print("Vision ready.", flush=True)
    yield

app = FastAPI(title="Coaster Vault Vision", lifespan=lifespan)

@app.get("/health")
def health():
    if model is None:
        raise HTTPException(503, "Model loading")
    return {"status": "ok", "modelVersion": MODEL_VERSION}

def embedding(image):
    import torch
    # Preserve the entire printed border. Average four quarter turns, then normalize.
    square = ImageOps.pad(image, (224, 224), color="white", method=Image.Resampling.LANCZOS)
    arr = np.array(square).astype(np.float32) / 255.0
    mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
    std = np.array([0.229, 0.224, 0.225], dtype=np.float32)
    batch = np.stack([((np.rot90(arr, k) - mean) / std).transpose(2, 0, 1) for k in range(4)])
    with lock, torch.inference_mode():
        vectors = model(pixel_values=torch.from_numpy(batch)).last_hidden_state[:, 0, :]
        vectors = torch.nn.functional.normalize(vectors, dim=1)
        pooled = torch.nn.functional.normalize(vectors.mean(dim=0), dim=0)
    return pooled.tolist()

@app.post("/analyze")
def analyze(file: UploadFile = File(...)):
    try:
        data = file.file.read(12 * 1024 * 1024 + 1)
        image, notes, blank = normalize(data)
    except ValueError as exc:
        raise HTTPException(400, str(exc)) from exc
    return {"embedding": embedding(image), "jpeg": base64.b64encode(jpeg_bytes(image)).decode(),
            "blank": blank, "warnings": notes, "modelVersion": MODEL_VERSION}

class Candidate(BaseModel):
    id: str
    front: str
    back: str
    swapped: bool

class Verification(BaseModel):
    front: str
    back: str
    candidates: list[Candidate] = Field(max_length=8)

@app.post("/verify")
def verify(request: Verification):
    try:
        front, back = safe_path(ROOT, request.front), safe_path(ROOT, request.back)
        results = []
        for item in request.candidates:
            cf, cb = safe_path(ROOT, item.front), safe_path(ROOT, item.back)
            if item.swapped:
                cf, cb = cb, cf
            score = pair_detail([geometric_similarity(front, cf), geometric_similarity(back, cb)])
            results.append({"id": item.id, "detailScore": score})
        return results
    except ValueError as exc:
        raise HTTPException(400, str(exc)) from exc
