using System.Text;
using System.Text.RegularExpressions;
namespace OncoMosaic;

public static class Ki67MapContract
{
    public static void Validate(string mapPath, string maskPath, Roi roi, IReadOnlyList<MeasuredCell> cells)
    {
        void Require(bool ok) { if (!ok) throw new ApiError(502, "INVALID_KI67_MAP", "Ki-67 定量图与核内测量不一致"); }
        BinaryReader Open(string path, string dtype)
        {
            var reader = new BinaryReader(File.OpenRead(path));
            try
            {
                Require(reader.ReadBytes(6).SequenceEqual(new byte[] { 0x93,78,85,77,80,89 }));
                var major = reader.ReadByte(); var minor = reader.ReadByte();
                Require(major is 1 or 2 && minor == 0);
                var size = major == 1 ? reader.ReadUInt16() : reader.ReadUInt32(); Require(size <= 8192);
                var header = Encoding.ASCII.GetString(reader.ReadBytes((int)size));
                var shape = Regex.Match(header, "'shape': \\((\\d+), (\\d+)\\)");
                Require(header.Contains($"'{dtype}'") && header.Contains("'fortran_order': False") && shape.Success && int.Parse(shape.Groups[1].Value) == roi.Height && int.Parse(shape.Groups[2].Value) == roi.Width);
                Require(reader.BaseStream.Length-reader.BaseStream.Position == (long)roi.Width*roi.Height*4);
                return reader;
            }
            catch { reader.Dispose(); throw; }
        }
        using var map = Open(mapPath,"<f4"); using var mask = Open(maskPath,"<i4");
        var sums = cells.ToDictionary(c => c.LocalIndex, _ => 0.0);
        for (int i=0; i<roi.Width*roi.Height; i++)
        {
            var value = map.ReadSingle(); var id = mask.ReadInt32();
            Require(float.IsFinite(value) && value is >= 0 and <= 1);
            if (id > 0) { Require(sums.ContainsKey(id)); sums[id] += value; }
        }
        Require(cells.All(c => c.Ki67Value is double v && Math.Abs(sums[c.LocalIndex]/c.AreaPx-v) < 1e-5));
    }
}
