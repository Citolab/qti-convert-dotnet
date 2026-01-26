# Citolab.QTI.Converter and Citolab.QTI.Uploader

Small .NET library that:

- validates and extracts QTI package zip files
- optionally converts QTI 2.x packages to QTI 3 (via pluggable converter)
- streams the original zip and extracted entries to a consumer-provided callback (e.g. Blob Storage)

## Packages / projects

- `Citolab.QTI.Uploader`: core uploader abstractions + zip extraction
- `Citolab.QTI.Converter`: optional (pure .NET) converter implementation that converts QTI 2.x packages to QTI 3

## Basic usage (ASP.NET Controller)

Create an `IQtiPackageStore` that uploads to your storage, then call `QtiPackageUploader.UploadAsync(...)`.

## QTI 2.x → QTI 3 conversion

If you want automatic conversion for QTI 2.x packages, configure `QtiUploaderOptions`:

```csharp
using Citolab.QTI.Converter;
using Citolab.QTI.Uploader;

var uploader = new QtiPackageUploader();
var options = new QtiUploaderOptions
{
    ConvertQti2ToQti3 = true,
    Converter = new Qti2ToQti3PackageConverter()
};
```

The converter uses the `qti2xTo30.xsl` XSLT 3.0 upgrader (embedded in the NuGet) when running on `net9.0`. For `netstandard2.0`, it falls back to a best-effort built-in conversion.
