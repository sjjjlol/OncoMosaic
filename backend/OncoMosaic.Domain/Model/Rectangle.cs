using System.Text.Json;
namespace OncoMosaic.Domain;

public record Rectangle(int X, int Y, int Width, int Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    public bool Valid(int width, int height) => X >= 0 && Y >= 0 && Width > 0 && Height > 0 && (long)X + Width <= width && (long)Y + Height <= height;
}
