using LitWeave.Services;

var client = new ZoteroClient();
try
{
    var result = await client.RefreshAsync(null);
    Console.WriteLine($"collections={result.Snapshot.Collections.Count}");
    Console.WriteLine($"items={result.Snapshot.Items.Count}");
    Console.WriteLine($"pdfAttachments={result.Snapshot.Items.SelectMany(item => item.Attachments).Count(attachment => attachment.IsPdf)}");
    Console.WriteLine($"hash={result.Snapshot.ContentHash}");
    Console.WriteLine($"baselineNew={result.Diff.NewItemKeys.Count}");
}
finally
{
    client.Dispose();
}
