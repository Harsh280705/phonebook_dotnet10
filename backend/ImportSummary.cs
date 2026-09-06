namespace Phonebook.Api;

public sealed class ImportRowError
{
    public int Row { get; set; }
    public string Error { get; set; } = "";
}

public sealed class ImportSummary
{
    public int TotalRows { get; set; }
    public int Imported { get; set; }
    public int SkippedDuplicates { get; set; }
    public int InvalidRows { get; set; }
    public int InvalidNames { get; set; }
    public int InvalidPhoneNumbers { get; set; }
    public int InvalidEmails { get; set; }
    public int InvalidAddresses { get; set; }
    public List<ImportRowError> RowErrors { get; } = [];
}
