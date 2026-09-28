import os
import zipfile
from pathlib import Path
from uuid import UUID
from fastapi import FastAPI
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field
from .model import MockSpectralAdapter, MODEL

app = FastAPI(title='OncoMosaic internal simulation service', version='1.0.0')
adapter = MockSpectralAdapter(Path(os.environ.get('STORE_ROOT', '/store')))

class StrictModel(BaseModel):
    model_config = ConfigDict(extra='forbid')

class ImageRequest(StrictModel):
    imageKey: str

class Roi(StrictModel):
    x: int = Field(ge=0)
    y: int = Field(ge=0)
    width: int = Field(gt=0)
    height: int = Field(gt=0)

class AnalyzeRequest(ImageRequest):
    runId: UUID
    roi: Roi
    modelVersion: str

class Metadata(StrictModel):
    width: int
    height: int
    bandCount: int
    wavelengths: list[float]
    pixelSizeUm: float
    previewKey: str

class Marker(StrictModel):
    name: str
    displayKey: str

class Manifest(StrictModel):
    runId: str
    modelVersion: str
    roi: Roi
    width: int
    height: int
    markers: list[Marker]
    maskKey: str
    overlayKey: str
    cellsKey: str
    cellCount: int
    identity: dict

@app.get('/v1/health')
def health():
    return {'status': 'ok', 'modelVersion': MODEL}

@app.post('/v1/inspect', response_model=Metadata)
def inspect(request: ImageRequest):
    return adapter.inspect(request.imageKey)

@app.post('/v1/analyze', response_model=Manifest)
def analyze(request: AnalyzeRequest):
    return adapter.analyze(request.imageKey, request.roi.model_dump(), str(request.runId), request.modelVersion)

@app.exception_handler(Exception)
async def invalid_input(_, error):
    if isinstance(error, (ValueError, OSError, KeyError, zipfile.BadZipFile, EOFError)):
        return JSONResponse(status_code=422, content={'code': 'INVALID_IMAGE', 'message': str(error)})
    return JSONResponse(status_code=500, content={'code': 'INFERENCE_ERROR', 'message': '模拟分析失败，请检查服务日志'})
