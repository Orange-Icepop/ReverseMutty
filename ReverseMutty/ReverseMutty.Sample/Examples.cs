using System.Collections.Generic;
using ReverseMutty;

namespace ReverseMutty.Sample;

// This code will not compile until you build the project with the Source Generators

[GenerateImmutable]
public class Examples
{
    public string Name { get; init; } = "Bible";
    public int Game { get; set; }
    public List<int> Numbers { get; set; } = [1, 1, 4, 5, 1, 4];
    public List<string> Strings { get; set; } = [""];

    [InImmutable]
    public bool IsMatch()
    {
        return Game.ToString() == Name;
    }
}