namespace Citolab.QTI.Uploader;

public sealed record QtiStoredFile(
    QtiStoredFileKind Kind,
    string RelativePath,
    Stream Content,
    QtiXmlKind XmlKind = QtiXmlKind.None);

