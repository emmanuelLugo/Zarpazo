namespace Zarpazo.Update;

/// <summary>An update step failed; the message is meant to be shown to the person as it is.</summary>
internal sealed class UpdateException(string message) : Exception(message);
