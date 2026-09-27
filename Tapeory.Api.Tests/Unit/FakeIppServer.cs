using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Tapeory.Api.Tests.Unit;

/// <summary>Stands in for a CUPS server: records each IPP request and answers with the next
/// canned response (repeating the last one), or by operation when given a responder.</summary>
internal sealed class FakeIppServer : HttpMessageHandler
{
    public const int PrintJob = 0x0002;
    public const int CancelJob = 0x0008;
    public const int GetJobAttributes = 0x0009;
    public const int GetPrinterAttributes = 0x000B;

    private readonly Func<int, byte[]> _respond;
    private int _next;

    public FakeIppServer(params byte[][] responses) =>
        _respond = _ => responses[Math.Min(_next++, responses.Length - 1)];

    public FakeIppServer(Func<int, byte[]> respondToOperation) => _respond = respondToOperation;

    public IEnumerable<int> Operations => Requests.Select(request => (request.Body[2] << 8) | request.Body[3]);

    public List<(Uri Uri, byte[] Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add((request.RequestUri!, requestBody));
        var body = _respond((requestBody[2] << 8) | requestBody[3]);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    public static bool Contains(byte[] data, byte[] sequence) =>
        data.AsSpan().IndexOf(sequence) >= 0;
}

/// <summary>Builds IPP response bodies.</summary>
internal static class IppResponses
{
    public static byte[] Printer(int state, bool acceptingJobs, string message = "", string? deviceUri = null) =>
        Build(0x0000, writer =>
        {
            writer.Enum("printer-state", state);
            writer.Boolean("printer-is-accepting-jobs", acceptingJobs);
            writer.Keyword("printer-state-reasons", state == 5 ? "paused" : "none");
            writer.Text("printer-state-message", message);

            if (deviceUri is not null)
            {
                writer.Uri("device-uri", deviceUri);
            }
        });

    public static byte[] Job(int jobId, int state, string message = "") => Build(0x0000, writer =>
    {
        writer.Integer("job-id", jobId);
        writer.Enum("job-state", state);
        writer.Text("job-state-message", message);
    });

    public static byte[] Error(int status, string message) => Build(status, writer => writer.Text("status-message", message));

    private static byte[] Build(int status, Action<Writer> attributes)
    {
        var writer = new Writer();
        writer.Bytes([0x02, 0x00, (byte)(status >> 8), (byte)status, 0, 0, 0, 1, 0x01]);
        attributes(writer);
        writer.Bytes([0x03]);
        return writer.ToArray();
    }

    private sealed class Writer
    {
        private readonly MemoryStream _stream = new();

        public void Bytes(byte[] bytes) => _stream.Write(bytes);

        public void Integer(string name, int value) => Attribute(0x21, name, Int(value));

        public void Enum(string name, int value) => Attribute(0x23, name, Int(value));

        public void Boolean(string name, bool value) => Attribute(0x22, name, [value ? (byte)1 : (byte)0]);

        public void Keyword(string name, string value) => Attribute(0x44, name, Encoding.UTF8.GetBytes(value));

        public void Uri(string name, string value) => Attribute(0x45, name, Encoding.UTF8.GetBytes(value));

        public void Text(string name, string value) => Attribute(0x41, name, Encoding.UTF8.GetBytes(value));

        public byte[] ToArray() => _stream.ToArray();

        private void Attribute(byte tag, string name, byte[] value)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            _stream.WriteByte(tag);
            _stream.Write([(byte)(nameBytes.Length >> 8), (byte)nameBytes.Length]);
            _stream.Write(nameBytes);
            _stream.Write([(byte)(value.Length >> 8), (byte)value.Length]);
            _stream.Write(value);
        }

        private static byte[] Int(int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            return bytes;
        }
    }
}
