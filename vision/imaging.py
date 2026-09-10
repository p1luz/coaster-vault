"""Image preparation and geometric verification; no model dependency here."""
from io import BytesIO
from pathlib import Path
import warnings
import cv2
import numpy as np
from PIL import Image, ImageOps, UnidentifiedImageError

Image.MAX_IMAGE_PIXELS = 30_000_000

def normalize(data: bytes) -> tuple[Image.Image, list[str], bool]:
    if not data or len(data) > 12 * 1024 * 1024:
        raise ValueError("Immagine vuota o oltre 12 MB")
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            with Image.open(BytesIO(data)) as source:
                if source.format not in {"JPEG", "PNG", "WEBP"}:
                    raise ValueError("Usa JPEG, PNG o WebP")
                source.load()
                fixed = ImageOps.exif_transpose(source).convert("RGBA")
                image = Image.new("RGBA", fixed.size, "white")
                image.alpha_composite(fixed)
                image = image.convert("RGB")
    except (UnidentifiedImageError, OSError, Image.DecompressionBombError, Image.DecompressionBombWarning) as exc:
        raise ValueError("Immagine non valida o troppo grande") from exc
    if min(image.size) < 100:
        raise ValueError("La foto deve avere almeno 100 pixel su ogni lato")
    image.thumbnail((1400, 1400), Image.Resampling.LANCZOS)
    gray = cv2.cvtColor(np.array(image), cv2.COLOR_RGB2GRAY)
    # Inspect the centre: a dark table around a blank reverse must not dominate.
    h, w = gray.shape
    centre = gray[h//5:4*h//5, w//5:4*w//5]
    blank = float(centre.std()) < 7.0
    notes = []
    if min(image.size) < 400:
        notes.append("Risoluzione bassa: meglio una foto più grande.")
    if not blank and cv2.Laplacian(gray, cv2.CV_64F).var() < 35:
        notes.append("Possibile sfocatura: controlla le scritte prima di continuare.")
    if blank:
        notes.append("Faccia poco informativa o uniforme: verifica che il ritaglio sia corretto.")
    return image, notes, blank

def jpeg_bytes(image: Image.Image) -> bytes:
    output = BytesIO()
    image.save(output, format="JPEG", quality=92)
    return output.getvalue()

def safe_path(root: Path, relative: str) -> Path:
    root = root.resolve()
    result = (root / relative).resolve()
    if not result.is_relative_to(root) or result.suffix.lower() != ".jpg":
        raise ValueError("Invalid media path")
    return result

def geometric_similarity(first: Path, second: Path) -> float | None:
    """RANSAC inlier evidence. None means insufficient texture, not mismatch."""
    a = cv2.imread(str(first), cv2.IMREAD_GRAYSCALE)
    b = cv2.imread(str(second), cv2.IMREAD_GRAYSCALE)
    if a is None or b is None:
        return None
    orb = cv2.ORB_create(nfeatures=1800)
    ka, da = orb.detectAndCompute(a, None)
    kb, db = orb.detectAndCompute(b, None)
    if da is None or db is None or min(len(ka), len(kb)) < 12:
        return None
    pairs = cv2.BFMatcher(cv2.NORM_HAMMING).knnMatch(da, db, k=2)
    good = [pair[0] for pair in pairs if len(pair) == 2 and pair[0].distance < 0.72 * pair[1].distance]
    if len(good) < 8:
        return 0.0
    pa = np.float32([ka[m.queryIdx].pt for m in good]).reshape(-1, 1, 2)
    pb = np.float32([kb[m.trainIdx].pt for m in good]).reshape(-1, 1, 2)
    matrix, mask = cv2.findHomography(pa, pb, cv2.RANSAC, 5.0)
    if matrix is None or mask is None:
        return 0.0
    inliers = int(mask.sum())
    # Require evidence spread across the image, not just one identical logo.
    points = pa[mask.ravel().astype(bool)]
    coverage = cv2.contourArea(cv2.convexHull(points)) / (a.shape[0] * a.shape[1]) if len(points) >= 3 else 0
    return float(np.clip((inliers / len(good)) * min(inliers / 50, 1) * min(coverage / 0.25, 1), 0, 1))

def pair_detail(scores: list[float | None]) -> float | None:
    usable = [x for x in scores if x is not None]
    if not usable:
        return None
    if len(usable) == 1:
        return usable[0]
    return 0.65 * min(usable) + 0.35 * max(usable)
