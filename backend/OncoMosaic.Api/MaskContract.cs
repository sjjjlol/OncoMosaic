using System.Text;
using System.Text.RegularExpressions;
namespace OncoMosaic;

public static class MaskContract
{
    // The adapter contract publishes little-endian int32 C-order NPY masks.
    public static void Validate(string path, Roi roi, IReadOnlyList<MeasuredCell> cells)
    {
        void Require(bool condition) { if (!condition) throw new ApiError(502, "INVALID_MASK", "核掩膜的尺寸、实例编号或面积与细胞记录不符"); }
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.ASCII);
        Require(reader.ReadBytes(6).SequenceEqual(new byte[] { 0x93, 78, 85, 77, 80, 89 }));
        var major = reader.ReadByte(); var minor = reader.ReadByte();
        Require(major is 1 or 2 && minor == 0);
        var size = major == 1 ? reader.ReadUInt16() : reader.ReadUInt32();
        Require(size <= 8192);
        var header = Encoding.ASCII.GetString(reader.ReadBytes((int)size));
        Require(header.Contains("'<i4'") && header.Contains("'fortran_order': False"));
        var shape = Regex.Match(header, "'shape': \\((\\d+), (\\d+)\\)");
        Require(shape.Success && int.Parse(shape.Groups[1].Value) == roi.Height && int.Parse(shape.Groups[2].Value) == roi.Width);
        Require(reader.BaseStream.Length-reader.BaseStream.Position == (long)roi.Width*roi.Height*4);
        var areas = cells.ToDictionary(c => c.LocalIndex, _ => 0);
        for (int i = 0; i < roi.Width * roi.Height; i++)
        {
            var id = reader.ReadInt32();
            if (id == 0) continue;
            Require(areas.ContainsKey(id)); areas[id]++;
        }
        Require(cells.All(c => areas[c.LocalIndex] == c.AreaPx));
    }
}
