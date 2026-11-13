# Third-party notices

Freezia includes the following third-party components. All of them run locally; none of them
connect to the internet.

| Component | Used for | License | Source |
|---|---|---|---|
| IBM Plex Sans Thai | UI and text-annotation font | SIL Open Font License 1.1 (`src/Freezia/Assets/Fonts/OFL.txt`) | https://github.com/IBM/plex |
| Tesseract OCR 5.5 (via the TesseractOCR .NET wrapper 5.5.2) | Text recognition (OCR) | Apache License 2.0 | https://github.com/tesseract-ocr/tesseract, https://github.com/Sicos1977/TesseractOCR |
| Leptonica 1.85 | Image library used by Tesseract | BSD 2-Clause | http://www.leptonica.org |
| tessdata_best (`tha`, `eng`) | Thai and English OCR models | Apache License 2.0 (`src/Freezia/Assets/Ocr/tessdata/LICENSE.txt`) | https://github.com/tesseract-ocr/tessdata_best |
| ZXing.Net 0.16 | QR code and barcode reading | Apache License 2.0 | https://github.com/micjahn/ZXing.Net |
| Microsoft Visual C++ 2015-2022 Redistributable (x64) | Runtime needed by the OCR engine; bundled with the installer and installed only when missing | Microsoft Software License Terms for the Visual C++ Redistributable | https://aka.ms/vs/17/release/vc_redist.x64.exe |

The installer is built with Inno Setup (https://jrsoftware.org/isinfo.php), which is free for
non-commercial use.
