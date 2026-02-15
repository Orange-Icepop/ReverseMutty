using System.Collections.Generic;

namespace ReverseMutty.Sample;

// This code will not compile until you build the project with the Source Generators

[GenerateImmutable]
public class Examples
{
    public string Name { get; init; } = "Bible";
    public int Game { get; set; }

    [InImmutable]
    public bool IsMatch()
    {
        return Game.ToString() == Name;
    }
}