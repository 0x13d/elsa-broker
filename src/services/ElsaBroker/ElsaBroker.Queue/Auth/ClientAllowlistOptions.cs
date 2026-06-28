namespace ElsaBroker.Queue.Auth;

public class ClientAllowlistOptions
{
    public List<ClientEntry> Clients { get; set; } = [];
}

public class ClientEntry
{
    public string ClientId    { get; set; } = default!;
    public string Thumbprint  { get; set; } = default!;  // SHA-1, uppercase, no spaces
    public string Description { get; set; } = string.Empty;
}
