using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;

namespace Tapeory.Api.Printing;

/// <summary>One IPP response: the status code and the attributes by name (all groups merged;
/// multi-valued attributes keep every value).</summary>
public sealed record IppResponse(int StatusCode, IReadOnlyDictionary<string, IReadOnlyList<object>> Attributes)
{
    /// <summary>Status codes 0x0000–0x00FF are the "successful" range.</summary>
    public bool IsSuccess => StatusCode <= 0x00FF;

    public int? Integer(string name) =>
        Attributes.TryGetValue(name, out var values) && values is [int value, ..] ? value : null;

    public string? Text(string name) =>
        Attributes.TryGetValue(name, out var values) && values is [string value, ..] ? value : null;

    public IEnumerable<string> Texts(string name) =>
        Attributes.TryGetValue(name, out var values) ? values.OfType<string>() : [];
}

/// <summary>
/// A minimal IPP/1.1 client (RFC 8010/8011) for what Tapeory needs from a print server such as
/// CUPS: submit a job, follow or cancel it, and read a queue's status. Requests are
/// plain HTTP POSTs of the binary IPP encoding.
/// </summary>
public sealed class IppClient(HttpClient http)
{
    public const string RawDocumentFormat = "application/vnd.cups-raw";

    private const byte OperationAttributesTag = 0x01;
    private const byte EndOfAttributesTag = 0x03;

    private const byte IntegerTag = 0x21;
    private const byte EnumTag = 0x23;
    private const byte KeywordTag = 0x44;
    private const byte UriTag = 0x45;
    private const byte CharsetTag = 0x47;
    private const byte NaturalLanguageTag = 0x48;
    private const byte MimeMediaTypeTag = 0x49;
    private const byte NameTag = 0x42;

    private int _requestId;

    public static Uri QueueUri(string host, int port, string queue) =>
        new($"http://{host}:{port}/printers/{Uri.EscapeDataString(queue)}");

    public Task<IppResponse> PrintJobAsync(Uri queueUri, string jobName, byte[] document, CancellationToken cancellationToken)
    {
        var request = new Request(0x0002, queueUri);
        request.Add(NameTag, "job-name", jobName);
        request.Add(MimeMediaTypeTag, "document-format", RawDocumentFormat);
        return SendAsync(queueUri, request, document, cancellationToken);
    }

    public Task<IppResponse> GetJobAttributesAsync(Uri queueUri, int jobId, CancellationToken cancellationToken)
    {
        var request = new Request(0x0009, queueUri);
        request.Add(IntegerTag, "job-id", jobId);
        request.AddKeywords("requested-attributes", "job-state", "job-state-reasons", "job-state-message");
        return SendAsync(queueUri, request, null, cancellationToken);
    }

    public Task<IppResponse> CancelJobAsync(Uri queueUri, int jobId, CancellationToken cancellationToken)
    {
        var request = new Request(0x0008, queueUri);
        request.Add(IntegerTag, "job-id", jobId);
        return SendAsync(queueUri, request, null, cancellationToken);
    }

    public Task<IppResponse> GetPrinterAttributesAsync(Uri queueUri, CancellationToken cancellationToken)
    {
        var request = new Request(0x000B, queueUri);
        request.AddKeywords(
            "requested-attributes",
            "printer-state", "printer-state-message", "printer-state-reasons", "printer-is-accepting-jobs",
            "document-format-supported", "printer-make-and-model");
        return SendAsync(queueUri, request, null, cancellationToken);
    }

    private async Task<IppResponse> SendAsync(Uri uri, Request request, byte[]? document, CancellationToken cancellationToken)
    {
        var body = request.Encode(Interlocked.Increment(ref _requestId));

        if (document is not null)
        {
            body = [.. body, .. document];
        }

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");

        using var response = await http.PostAsync(uri, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    internal static IppResponse Parse(byte[] data)
    {
        if (data.Length < 8)
        {
            throw new InvalidDataException("The IPP response is too short.");
        }

        var status = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        var attributes = new Dictionary<string, List<object>>();
        var position = 8;
        string? currentName = null;

        while (position < data.Length)
        {
            var tag = data[position++];

            if (tag == EndOfAttributesTag)
            {
                break;
            }

            if (tag < 0x10)
            {
                continue; // start of another attribute group
            }

            var nameLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
            position += 2;
            var name = Encoding.UTF8.GetString(data, position, nameLength);
            position += nameLength;

            var valueLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
            position += 2;
            var value = data.AsSpan(position, valueLength);
            position += valueLength;

            // An empty name adds another value to the attribute before it.
            if (nameLength > 0)
            {
                currentName = name;
            }

            if (currentName is null)
            {
                continue;
            }

            object decoded = tag switch
            {
                IntegerTag or EnumTag when valueLength == 4 => BinaryPrimitives.ReadInt32BigEndian(value),
                0x22 when valueLength == 1 => value[0] != 0, // boolean
                >= 0x30 and < 0x40 => value.ToArray(), // octetString family (dates, resolutions, …)
                _ => Encoding.UTF8.GetString(value)
            };

            if (!attributes.TryGetValue(currentName, out var values))
            {
                attributes[currentName] = values = [];
            }

            values.Add(decoded);
        }

        return new IppResponse(
            status, attributes.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<object>)pair.Value));
    }

    private sealed class Request
    {
        private readonly MemoryStream _attributes = new();
        private readonly ushort _operation;

        public Request(ushort operation, Uri queueUri)
        {
            _operation = operation;
            Add(CharsetTag, "attributes-charset", "utf-8");
            Add(NaturalLanguageTag, "attributes-natural-language", "en");
            Add(UriTag, "printer-uri", new UriBuilder(queueUri) { Scheme = "ipp" }.Uri.ToString());
            Add(NameTag, "requesting-user-name", "tapeory");
        }

        public void Add(byte tag, string name, string value) => Write(tag, name, Encoding.UTF8.GetBytes(value));

        public void Add(byte tag, string name, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            Write(tag, name, bytes);
        }

        public void AddKeywords(string name, params string[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                Write(KeywordTag, i == 0 ? name : "", Encoding.UTF8.GetBytes(values[i]));
            }
        }

        private void Write(byte tag, string name, byte[] value)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            _attributes.WriteByte(tag);
            WriteUInt16(_attributes, nameBytes.Length);
            _attributes.Write(nameBytes);
            WriteUInt16(_attributes, value.Length);
            _attributes.Write(value);
        }

        public byte[] Encode(int requestId)
        {
            using var output = new MemoryStream();
            output.Write([0x02, 0x00]); // IPP/2.0, which CUPS answers like 1.1
            WriteUInt16(output, _operation);
            var id = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(id, requestId);
            output.Write(id);
            output.WriteByte(OperationAttributesTag);
            _attributes.WriteTo(output);
            output.WriteByte(EndOfAttributesTag);
            return output.ToArray();
        }

        private static void WriteUInt16(Stream stream, int value) =>
            stream.Write([(byte)(value >> 8), (byte)value]);
    }
}
