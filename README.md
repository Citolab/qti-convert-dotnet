# Citolab.QTI.Uploader

Small .NET library that:
- validates and extracts QTI package zip files
- optionally converts QTI 2.x packages to QTI 3 (via pluggable converter)
- streams the original zip and extracted entries to a consumer-provided callback (e.g. Blob Storage)

## Packages / projects

- `Citolab.QTI.Uploader`: core uploader abstractions + zip extraction
- `Citolab.QTI.Uploader.NpxConverter`: optional converter implementation that shells out to:
  `npx -p=@citolab/qti-convert qti-convert-pkg yourpackage.zip`

## Basic usage (ASP.NET Controller)

Create an `IQtiPackageStore` that uploads to your storage, then call `QtiPackageUploader.UploadAsync(...)`.

