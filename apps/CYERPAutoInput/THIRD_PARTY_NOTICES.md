# Third-Party Notices

CYERPAutoInput uses third-party components for local OCR inference.

## PaddleOCR / PP-OCRv5 models

- Project: PaddleOCR / PaddlePaddle
- License: Apache License 2.0
- Models used: `ch_PP-OCRv5_det_mobile` (detection), `ch_PP-OCRv5_rec_mobile` (recognition), `ch_PP-LCNet_x0_25_textline_ori_cls_mobile` (text-line orientation).
- Model files, sizes and SHA-256 are pinned in `tools/ocr-models.json`; model binaries are not committed to this public repository. Builds download them from this repository's OCR model Release, or from the pinned upstream revision when that Release asset is unavailable.

## RapidOCRSharpOnnx

- Project: `meloht/RapidOCRSharpOnnx`
- License: Apache License 2.0
- Used as the C# PP-OCR / ONNX Runtime integration layer.

## ONNX Runtime

- Project: Microsoft ONNX Runtime
- License: MIT License

## OpenCvSharp

- Project: OpenCvSharp
- License: BSD 3-Clause License

This file is informational and does not replace the license files distributed by the respective upstream projects or NuGet packages.
