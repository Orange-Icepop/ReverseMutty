using System.Collections.Generic;
using System.Text;
using ReverseMutty;

namespace ReverseMutty.Sample;

// This code will not compile until you build the project with the Source Generators

[GenerateImmutable]
public class Examples
{
    public string Name { get; } = "Bible";
    public int Game { get; set; }
    public List<int> Numbers { get; set; } = [1, 1, 4, 5, 1, 4];
    public List<string> Strings { get; set; } = [""];
    public Dictionary<string, int> Dictionary { get; set; } = new Dictionary<string, int>();

    [InImmutable]
    public bool IsMatch()
    {
        var match = new StringBuilder().AppendJoin(" ", Numbers);
        this.ToImmutable();
        return match.ToString() == Name;
    }
}