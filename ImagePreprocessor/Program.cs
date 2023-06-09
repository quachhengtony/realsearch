namespace ImagePreprocessor;

using System;
using System.IO;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class Program
{
    private const string InputFolderPath = @"myntradataset\images";
    private const string OutputFolderPath = @"myntradataset\preprocessed-images";
    private const int TargetImageSize = 224;

    static void Main(string[] args)
    {
        Directory.CreateDirectory(OutputFolderPath);

        string[] imagePaths = Directory.GetFiles(InputFolderPath, "*.jpg");
        foreach (var imagePath in imagePaths)
        {
            try
            {
                var preprocessedImage = PreprocessImage(imagePath);
                string outputFilePath = GetOutputFilePath(imagePath);
                SaveImage(preprocessedImage, outputFilePath);

                Console.WriteLine($"Preprocessed image saved: {outputFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image: {imagePath}");
                Console.WriteLine(ex.Message);
            }
        }
    }

    static Image<Rgb24> PreprocessImage(string imagePath)
    {
        using var image = Image.Load<Rgb24>(imagePath);

        image.Mutate(i => i.Resize(new ResizeOptions
        {
            Size = new Size(TargetImageSize, TargetImageSize),
            Mode = ResizeMode.Pad
        }));

        Tensor<float> input = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
        var mean = new[] { 0.485f, 0.456f, 0.406f };
        var stddev = new[] { 0.229f, 0.224f, 0.225f };
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelSpan = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    input[0, 0, y, x] = ((pixelSpan[x].R / 255f) - mean[0]) / stddev[0];
                    input[0, 1, y, x] = ((pixelSpan[x].G / 255f) - mean[1]) / stddev[1];
                    input[0, 2, y, x] = ((pixelSpan[x].B / 255f) - mean[2]) / stddev[2];
                }
            }
        });

        return image.Clone();
    }

    static void SaveImage(Image<Rgb24> image, string outputPath)
    {
        image.Save(outputPath, new PngEncoder());
    }

    static string GetOutputFilePath(string imagePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(imagePath);
        string outputFileName = $"{fileName}.png";
        string outputFilePath = Path.Combine(OutputFolderPath, outputFileName);

        return outputFilePath;
    }
}