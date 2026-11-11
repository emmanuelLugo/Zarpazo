namespace Zarpazo.Update;

/// <summary>The app can't restart now because the person is in the middle of a capture, an edit or a recording.</summary>
internal sealed class UpdateBusyException()
    : UpdateException("Estás usando Zarpazo ahora mismo (captura, edición o grabación). Se reiniciará en cuanto termines.");
