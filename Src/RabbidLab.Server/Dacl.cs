using System.Buffers.Binary;

namespace RabbidLab.Server;

/// <summary>
/// Gives an account the rights Delegate Control gives for "create, delete and manage user accounts" and the same for
/// groups and computers, by adding access entries to the DACL of a domain or OU (a self-relative security descriptor, as Active
/// Directory sends nTSecurityDescriptor). Written by hand so it works the same in Docker as on Windows.
/// </summary>
public static class Dacl
{
    private static readonly Guid UserClass = new("bf967aba-0de6-11d0-a285-00aa003049e2");
    private static readonly Guid GroupClass = new("bf967a9c-0de6-11d0-a285-00aa003049e2");
    private static readonly Guid ComputerClass = new("bf967a86-0de6-11d0-a285-00aa003049e2");

    private const byte AllowedObject = 0x05;
    private const byte ContainerInherit = 0x02;
    private const byte InheritOnly = 0x08;
    private const byte Inherited = 0x10;
    private const uint CreateChild = 0x1;
    private const uint DeleteChild = 0x2;
    private const uint FullControl = 0x000F01FF;
    private const uint ObjectTypePresent = 0x1;
    private const uint InheritedObjectTypePresent = 0x2;

    /// <summary>The entries that let this SID make, delete and fully manage users, groups and computers below the object.</summary>
    public static List<byte[]> Delegation(byte[] sid) =>
    [
        Ace(ContainerInherit, CreateChild | DeleteChild, UserClass, null, sid),
        Ace(ContainerInherit | InheritOnly, FullControl, null, UserClass, sid),
        Ace(ContainerInherit, CreateChild | DeleteChild, GroupClass, null, sid),
        Ace(ContainerInherit | InheritOnly, FullControl, null, GroupClass, sid),
        Ace(ContainerInherit, CreateChild | DeleteChild, ComputerClass, null, sid),
        Ace(ContainerInherit | InheritOnly, FullControl, null, ComputerClass, sid),
    ];

    /// <summary>The descriptor with the entries added after its own explicit ones (where Windows keeps them); entries already there aren't added twice.</summary>
    public static byte[] Grant(byte[] descriptor, IReadOnlyList<byte[]> entries)
    {
        if (descriptor.Length < 20 || descriptor[0] != 1)
        {
            throw new FormatException("That isn't a security descriptor.");
        }
        var control = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(2));
        var owner = Part(descriptor, 4, Sid);
        var group = Part(descriptor, 8, Sid);
        var sacl = Part(descriptor, 12, Acl);
        var dacl = Part(descriptor, 16, Acl) ?? EmptyAcl();
        var aces = Aces(dacl);
        var at = aces.FindIndex(a => (a[1] & Inherited) != 0);
        if (at < 0)
        {
            at = aces.Count;
        }
        foreach (var e in entries.Where(e => !aces.Any(a => a.AsSpan().SequenceEqual(e))))
        {
            aces.Insert(at++, e);
        }
        var total = 8 + aces.Sum(a => a.Length);
        if (total > ushort.MaxValue)
        {
            throw new FormatException("The DACL has no room for more entries.");
        }
        var newDacl = new List<byte>(total);
        var size = (ushort) total;
        newDacl.AddRange([Math.Max(dacl[0], (byte) 4), 0, (byte) size, (byte) (size >> 8), (byte) aces.Count, (byte) (aces.Count >> 8), 0, 0]);
        aces.ForEach(newDacl.AddRange);

        // Rebuilt: header, then SACL, DACL, owner and group, each where the header says.
        var output = new List<byte>(new byte[20]);
        output[0] = 1;
        control |= 0x4 | 0x8000; // DACL present, self-relative
        var offsets = new uint[4];
        void Put(int slot, byte[]? part)
        {
            if (part is null)
            {
                return;
            }
            offsets[slot] = (uint) output.Count;
            output.AddRange(part);
        }
        Put(2, sacl);
        Put(3, [.. newDacl]);
        Put(0, owner);
        Put(1, group);
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), control);
        for (var i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + (i * 4)), offsets[i]);
        }
        return result;
    }

    /// <summary>An ACCESS_ALLOWED_OBJECT_ACE.</summary>
    public static byte[] Ace(byte flags, uint mask, Guid? objectType, Guid? inheritedType, byte[] sid)
    {
        var size = 4 + 4 + 4 + (objectType is null ? 0 : 16) + (inheritedType is null ? 0 : 16) + sid.Length;
        var ace = new byte[size];
        ace[0] = AllowedObject;
        ace[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(ace.AsSpan(2), (ushort) size);
        BinaryPrimitives.WriteUInt32LittleEndian(ace.AsSpan(4), mask);
        BinaryPrimitives.WriteUInt32LittleEndian(ace.AsSpan(8), (objectType is null ? 0 : ObjectTypePresent) | (inheritedType is null ? 0 : InheritedObjectTypePresent));
        var at = 12;
        foreach (var g in new[] { objectType, inheritedType })
        {
            if (g is { } guid)
            {
                guid.ToByteArray().CopyTo(ace, at);
                at += 16;
            }
        }
        sid.CopyTo(ace, at);
        return ace;
    }

    private static byte[]? Part(byte[] sd, int offsetAt, Func<byte[], int, int> length)
    {
        var offset = (int) BinaryPrimitives.ReadUInt32LittleEndian(sd.AsSpan(offsetAt));
        if (offset == 0 || offset >= sd.Length)
        {
            return null;
        }
        var n = length(sd, offset);
        return offset + n > sd.Length ? throw new FormatException("The security descriptor is cut short.") : sd[offset..(offset + n)];
    }

    private static int Sid(byte[] b, int at) => 8 + (4 * b[at + 1]);

    private static int Acl(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 2));

    private static List<byte[]> Aces(byte[] acl)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(acl.AsSpan(4));
        var list = new List<byte[]>(count);
        var at = 8;
        for (var i = 0; i < count && at + 4 <= acl.Length; i++)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(acl.AsSpan(at + 2));
            if (size < 4 || at + size > acl.Length)
            {
                throw new FormatException("The DACL is cut short.");
            }
            list.Add(acl[at..(at + size)]);
            at += size;
        }
        return list;
    }

    private static byte[] EmptyAcl() => [4, 0, 8, 0, 0, 0, 0, 0];
}
