using System.Buffers.Binary;
using System.Text;

namespace Inventory.Tests;

// Minimal REGF fixtures are encoded independently from the production reader.
// They contain only test data and need no host registry or Windows ACL API.
internal sealed class BinaryHiveFixture
{
    internal sealed record Expand(string Text);
    internal sealed class Key(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, Key> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Key Sub(string path)
        {
            var current = this;
            foreach (var part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!current.Children.TryGetValue(part, out var child)) current.Children[part] = child = new(part);
                current = child;
            }
            return current;
        }
    }
    public Key Root { get; } = new("ROOT");
    private readonly Dictionary<int, byte[]> cells = new();
    private int next = 32;
    private static void I(byte[] data, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
    private static void S(byte[] data, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
    private int Add(byte[] body)
    {
        int index = next; cells.Add(index, body);
        next += (body.Length + 4 + 7) & ~7;
        return index;
    }
    private int Value(string name, object value)
    {
        var encodedName = Encoding.ASCII.GetBytes(name);
        var record = new byte[20 + encodedName.Length];
        record[0] = (byte)'v'; record[1] = (byte)'k'; S(record, 2, (ushort)encodedName.Length);
        S(record, 16, 1); encodedName.CopyTo(record, 20);
        if (value is int number)
        { I(record, 4, unchecked((int)0x80000004)); I(record, 8, number); I(record, 12, 4); }
        else
        {
            int type = value is string[] ? 7 : value is Expand ? 2 : 1;
            var text = value is string[] list ? string.Join("\0", list) + "\0\0" :
                value is Expand expand ? expand.Text + "\0" : value + "\0";
            var bytes = Encoding.Unicode.GetBytes(text);
            I(record, 4, bytes.Length); I(record, 8, Add(bytes)); I(record, 12, type);
        }
        return Add(record);
    }
    private int Node(Key node, bool isRoot)
    {
        var children = node.Children.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => Node(pair.Value, false)).ToArray();
        int subkeys = -1, values = -1;
        if (children.Length != 0)
        {
            var list = new byte[4 + 4 * children.Length]; list[0] = (byte)'l'; list[1] = (byte)'i';
            S(list, 2, (ushort)children.Length);
            for (int i = 0; i < children.Length; i++) I(list, 4 + 4 * i, children[i]);
            subkeys = Add(list);
        }
        if (node.Values.Count != 0)
        {
            var indices = node.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => Value(pair.Key, pair.Value)).ToArray();
            var list = new byte[4 * indices.Length];
            for (int i = 0; i < indices.Length; i++) I(list, 4 * i, indices[i]);
            values = Add(list);
        }
        var name = Encoding.ASCII.GetBytes(node.Name);
        var body = new byte[76 + name.Length]; body[0] = (byte)'n'; body[1] = (byte)'k';
        S(body, 2, (ushort)(isRoot ? 0x24 : 0x20));
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(4), DateTime.UtcNow.ToFileTimeUtc());
        I(body, 16, -1); I(body, 20, children.Length); I(body, 28, subkeys); I(body, 32, -1);
        I(body, 36, node.Values.Count); I(body, 40, values); I(body, 44, -1); I(body, 48, -1);
        S(body, 72, (ushort)name.Length); name.CopyTo(body, 76);
        int index = Add(body);
        foreach (var child in children) I(cells[child], 16, index);
        return index;
    }
    public byte[] Build()
    {
        cells.Clear(); next = 32;
        int root = Node(Root, true);
        int length = (next + 4095) & ~4095;
        var data = new byte[4096 + length];
        Encoding.ASCII.GetBytes("regf").CopyTo(data, 0); I(data, 4, 1); I(data, 8, 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(12), DateTime.UtcNow.ToFileTimeUtc());
        I(data, 20, 1); I(data, 24, 3); I(data, 32, 1); I(data, 36, root); I(data, 40, length);
        int checksum = 0;
        for (int offset = 0; offset < 508; offset += 4) checksum ^= BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        I(data, 508, checksum);
        Encoding.ASCII.GetBytes("hbin").CopyTo(data, 4096); I(data, 4104, length);
        foreach (var (index, body) in cells)
        {
            int size = (body.Length + 11) & ~7;
            I(data, 4096 + index, -size); body.CopyTo(data, 4100 + index);
        }
        if (length > next) I(data, 4096 + next, length - next);
        return data;
    }
}
