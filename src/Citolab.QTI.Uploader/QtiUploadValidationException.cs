namespace Citolab.QTI.Uploader;

public class QtiUploadValidationException : Exception
{
    public QtiUploadValidationException() { }
    public QtiUploadValidationException(string message) : base(message) { }
    public QtiUploadValidationException(string message, Exception inner) : base(message, inner) { }
}
