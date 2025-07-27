using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clevernet.Data;

public class File
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(1024)]
    public required string Path { get; set; }
    
    [NotMapped]
    public string SharePath => $"{Share}:/{Path}";

    [Required]
    [MaxLength(64)]
    public required string Owner { get; set; }
    
    [Required]
    [MaxLength(64)]
    public required string Share { get; set; }
    
    [Required]
    public required string ContentType { get; set; }
    
    // Split content into text and binary
    public string? TextContent { get; set; }
    
    public byte[]? BinaryContent { get; set; }

    [NotMapped]
    public string OpenFileContent => TextContent ?? ($"Binary file ({BinaryContent?.Length ?? 0} bytes)");
    
    
    public DateTimeOffset CreatedAt { get; set; }
    
    public DateTimeOffset UpdatedAt { get; set; }
}