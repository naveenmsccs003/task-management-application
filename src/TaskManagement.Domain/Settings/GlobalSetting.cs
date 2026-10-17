namespace TaskManagement.Domain.Settings;

/// <summary>
/// One system-wide setting, stored as JSON. Typed access and validation: <c>SettingsService</c> in the Application layer.
/// </summary>
public sealed class GlobalSetting
{
    private GlobalSetting()
    {
        Key = string.Empty;
        ValueJson = "null";
    }

    public GlobalSetting(string key, string valueJson, DateTimeOffset updatedAt, Guid? updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueJson);
        Key = key;
        ValueJson = valueJson;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    public const int KeyMaxLength = 150;

    public string Key { get; private set; }

    public string ValueJson { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Update(string valueJson, DateTimeOffset updatedAt, Guid? updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueJson);
        ValueJson = valueJson;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }
}
