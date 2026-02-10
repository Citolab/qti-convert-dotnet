# Citolab.QTI.Converter and Citolab.QTI.Uploader

Small .NET library that:

- validates and extracts QTI package zip files
- optionally converts QTI 2.x packages to QTI 3 (via pluggable converter)
- streams the original zip and extracted entries to a consumer-provided callback (e.g. Blob Storage)

## Packages / projects

- `Citolab.QTI.Uploader`: core uploader abstractions + zip extraction
- `Citolab.QTI.Converter`: optional (pure .NET) converter implementation that converts QTI 2.x packages to QTI 3

## NuGet publishing (GitHub Actions)

This repo includes a GitHub Actions workflow at `.github/workflows/nuget.yml` that builds/tests/packs on PRs and on pushes to `main`, and publishes to NuGet.org on:

- a tag push like `v0.1.0` (uses the tag as the NuGet version), or
- a manual run (Actions → workflow → Run workflow) with an optional `version` input.

To enable publishing, add `NUGET_API_KEY` as a repository secret.

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
    Converter = new Qti2ToQti3PackageConverter(
        new Qti2ToQti3PackageConverterOptions
        {
            ItemTransformOptions = new QtiItemTransformOptions
            {
                // These are enabled by default; set to false to disable.
                ObjectToImg = true,
                ObjectToVideo = true,
                ObjectToAudio = true,
                SsmlSubToSpan = true,
                StripMaterialInfo = true,
                MinChoicesToOne = true,
                ExternalScored = true,

                // Optional extras (off by default).
                QbCleanup = false,
                DepConvert = false,
                UpgradePci = false,
                StripStylesheets = false
            },

            // Optional per-item hook for consumer-specific transformations.
            OnItemTransformedAsync = async (transform, itemPath, ct) =>
            {
                // Example: add a marker attribute
                await transform.FnChAsync(doc =>
                {
                    doc.Root?.SetAttributeValue("data-processed-by", "my-app");
                    return Task.CompletedTask;
                });
            }
        })
};
```

The converter uses the `qti2xTo30.xsl` XSLT 3.0 upgrader (embedded in the NuGet) when running on `net9.0`. For `netstandard2.0`, it falls back to a best-effort built-in conversion.

## Item transformations

When converting an assessment item, the converter can apply a set of optional XML transformations via `QtiTransform` and `QtiItemTransformOptions`.

**Default item transforms (enabled by default)**
- `ObjectToImg`: converts `<object type="image/*" data="...">ALT</object>` to `<img src="..." alt="ALT" .../>`.
- `ObjectToVideo`: converts `<object type="video/*" ... data="...">` to `<video><source .../></video>` and normalizes existing `<video controls>` usage.
- `ObjectToAudio`: converts `<object type="audio/*" ... data="...">` to `<audio><source .../></audio>` and normalizes existing `<audio controls>` usage.
- `SsmlSubToSpan`: converts SSML elements (e.g. `<ssml:sub>`, `<ssml:break>`, `<ssml:say-as>`, etc.) to `<span>` elements with `data-ssml-*` attributes.
- `StripMaterialInfo`: removes `<qti-companion-materials-info>`.
- `MinChoicesToOne`: ensures `<qti-choice-interaction min-choices>` is at least `1`.
- `ExternalScored`: when there is no `<qti-response-processing>`, sets `external-scored="human"` on the `SCORE` outcome declaration.

**Additional item transforms (disabled by default)**
- `QbCleanup`: cleanup for common Question Builder (QB) HTML-ish wrappers (unwraps some `<div>` wrappers, span cleanup, etc.).
- `DepConvert`: converts Dutch Extension Profile dialog triggers (`.dep-dialogTrigger`) into HTML `popover` buttons.
- `DepConvertExtended`: converts DEP dialogs into a `<dep-popup>` structure (trigger + popup slot).
- `HideInputsForChoiceInteractionWithImages`: adds `qti-input-control-hidden` when all choices contain images.
- `UpgradePci`: upgrades TAO-exported portable custom interactions into a structure expected by QTI components (moves properties to `data-*`, normalizes modules/markup, etc.).
- `StripStylesheets`: removes `<qti-stylesheet>` nodes; supports wildcard matching via `StripStylesheetsRemovePattern` / `StripStylesheetsKeepPattern`.
- `StylesheetsInlineAsync`: fetches CSS content from external stylesheet URLs and inlines them into `<qti-stylesheet>` elements. Supports both HTTP URLs and relative file paths within QTI packages. Use `OnItemTransformedAsync` hook for custom async transformations like this.

**Usage example for StylesheetsInlineAsync with ZIP uploads:**
```csharp
OnItemTransformedAsync = async (transform, itemPath, fileResolver, ct) =>
{
    // Inline external stylesheets from ZIP package files
    await transform.StylesheetsInlineAsync(async (resolvedPath, currentItemPath) =>
    {
        // resolvedPath is the stylesheet path resolved relative to the item
        // Use the provided fileResolver to get CSS content from ZIP package
        return await fileResolver(resolvedPath);
    }, itemPath);
    
    // Alternative: if stylesheets are HTTP URLs, use custom HTTP client
    await transform.StylesheetsInlineAsync(async href => {
        if (href.StartsWith("http"))
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(href, ct);
            return response.IsSuccessStatusCode 
                ? await response.Content.ReadAsStringAsync() 
                : null;
        }
        // For relative paths, use the ZIP file resolver
        var resolvedPath = Path.Combine(Path.GetDirectoryName(itemPath) ?? "", href.Replace('/', Path.DirectorySeparatorChar));
        return await fileResolver(resolvedPath.Replace('\\', '/'));
    });
}
```

**Example QTI Package Structure:**
```
item1/
  ├── question.xml          (contains <qti-stylesheet href="../shared/styles.css">)
  └── item-specific.css
shared/
  └── styles.css            (CSS file to be inlined)
```

When processing `item1/question.xml`, the stylesheet `../shared/styles.css` resolves to `shared/styles.css` in the ZIP package.

**Consumer hook**
- `Qti2ToQti3PackageConverterOptions.OnItemTransformedAsync` runs per assessment item after the built-in transforms, and receives a mutable `QtiTransform` so you can call `.FnCh(...)` / `.FnChAsync(...)` or chain additional built-ins.
