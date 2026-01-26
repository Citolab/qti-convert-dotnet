namespace Citolab.QTI.Uploader;

public sealed class QtiUploaderOptions
{
    public long MaxPackageBytes { get; set; } = 100L * 1024 * 1024;
    public bool StoreOriginalPackage { get; set; } = true;
    public bool StoreConvertedPackage { get; set; } = false;
    public bool CleanAndPrettyPrintQtiXml { get; set; } = true;

    public bool ConvertQti2ToQti3 { get; set; } = false;
    public IQtiPackageConverter? Converter { get; set; }
}

