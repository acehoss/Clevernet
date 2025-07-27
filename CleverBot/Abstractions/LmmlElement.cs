using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

public class LmmlElement
{
    public const string BooleanTrueValue = "booleantrue";
    public string Tag { get; }
    
    public LmmlElement(string tag) { Tag = tag; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public LmmlContent? Content { get; set; }

    public override string ToString() => ToString(0);
    
    public string ToString(int nestingLevel = 0)
    {
        var sb = new StringBuilder();
        //we assume parent element has established indent for tag opening
        sb.Append($"<{Tag}");
        
        foreach (var (key, value) in Attributes)
        {
            if (value == BooleanTrueValue)
                sb.Append($" {key}");
            else
                sb.Append($" {key}=\"{System.Security.SecurityElement.Escape(value)}\"");
        }

        if (Content is null || Content is LmmlStringContent str && string.IsNullOrEmpty(str.Content) || Content is LmmlChildContent child && child.Children.Count == 0)
        {
            sb.Append("/>");
        } 
        else
        {
            sb.Append(">");
            if (Content is LmmlStringContent strContent)
            {
                sb.Append(strContent.Raw ? strContent.Content : System.Security.SecurityElement.Escape(strContent.Content));
                sb.Append($"</{Tag}>");
            }
            else
            {
                sb.AppendLine();
                //indent start of child element
                for (var i = 0; i <= nestingLevel; i++)
                    sb.Append(" ");
                sb.Append(Content.ToString(nestingLevel + 1));
                sb.AppendLine();
                for (var i = 0; i < nestingLevel; i++)
                    sb.Append(" ");
                sb.Append($"</{Tag}>");
            }
        }

        return sb.ToString();
    }

    public static LmmlElement Parse(string lmml)
    {
        lmml = lmml.Trim();
        if (!lmml.StartsWith("<") || !lmml.EndsWith(">"))
            throw new ArgumentException("LMML must start with < and end with >", nameof(lmml));

        // Handle self-closing tags
        if (lmml.EndsWith("/>"))
        {
            var tagAndAttrs = lmml[1..^2].Trim(); // Remove < and />
            return ParseTagAndAttributes(tagAndAttrs);
        }

        // Find the tag name and end of opening tag
        var firstSpaceOrClose = lmml.IndexOfAny(new[] { ' ', '>' });
        if (firstSpaceOrClose == -1) throw new ArgumentException("Invalid LMML format", nameof(lmml));
        
        var tagName = lmml[1..firstSpaceOrClose];
        var element = new LmmlElement(tagName);
        
        // Find where the opening tag ends
        var openTagEnd = lmml.IndexOf('>', firstSpaceOrClose);
        if (openTagEnd == -1) throw new ArgumentException("Invalid LMML format - no closing bracket", nameof(lmml));
        
        // Parse attributes if any
        if (openTagEnd > firstSpaceOrClose + 1)
        {
            var attributesStr = lmml[(firstSpaceOrClose + 1)..openTagEnd].Trim();
            ParseAttributes(attributesStr, element.Attributes);
        }

        // Find the closing tag
        var closingTag = $"</{tagName}>";
        var closingTagIndex = lmml.LastIndexOf(closingTag);
        if (closingTagIndex == -1) throw new ArgumentException($"No closing tag found for {tagName}", nameof(lmml));

        // Extract content between tags
        var content = lmml[(openTagEnd + 1)..closingTagIndex].Trim();
        
        if (string.IsNullOrEmpty(content))
        {
            element.Content = null;
        }
        else if (content.StartsWith("<"))
        {
            // Has child elements
            var childContent = new LmmlChildContent();
            var remaining = content;
            
            while (!string.IsNullOrWhiteSpace(remaining))
            {
                remaining = remaining.TrimStart();
                if (!remaining.StartsWith("<")) break;
                
                // Find the end of this child element
                var childElement = ParseNextElement(ref remaining);
                childContent.Children.Add(childElement);
            }
            
            element.Content = childContent;
        }
        else
        {
            // Just string content
            element.Content = new LmmlStringContent(UnescapeXml(content));
        }

        return element;
    }

    private static LmmlElement ParseTagAndAttributes(string tagAndAttrs)
    {
        var parts = tagAndAttrs.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var element = new LmmlElement(parts[0]);
        
        if (parts.Length > 1)
        {
            ParseAttributes(parts[1].Trim(), element.Attributes);
        }
        
        return element;
    }

    private static void ParseAttributes(string attributesStr, Dictionary<string, string> attributes)
    {
        var remaining = attributesStr;
        while (!string.IsNullOrWhiteSpace(remaining))
        {
            remaining = remaining.TrimStart();
            if (remaining.Length == 0) break;

            // Handle boolean attributes (just the name means true)
            var nextSpace = remaining.IndexOf(' ');
            if (nextSpace == -1)
            {
                // Last attribute
                if (!remaining.Contains('='))
                {
                    attributes[remaining] = BooleanTrueValue;
                }
                else
                {
                    // Regular attribute with value
                    var lastEquals = remaining.IndexOf('=');
                    var lastName = remaining[..lastEquals].Trim();
                    remaining = remaining[(lastEquals + 1)..].TrimStart();
                    
                    if (!remaining.StartsWith("\""))
                        throw new ArgumentException("Attribute values must be quoted");
                        
                    var lastEndQuote = remaining.IndexOf("\"", 1);
                    if (lastEndQuote == -1)
                        throw new ArgumentException("Unterminated attribute value");
                        
                    var lastValue = remaining[1..lastEndQuote];
                    attributes[lastName] = UnescapeXml(lastValue);
                }
                break;
            }

            var equalsBeforeSpace = remaining.IndexOf('=', 0, nextSpace);
            if (equalsBeforeSpace == -1)
            {
                // Boolean attribute
                var attrName = remaining[..nextSpace];
                attributes[attrName] = BooleanTrueValue;
                remaining = remaining[nextSpace..];
                continue;
            }

            // Regular attribute with value
            var name = remaining[..equalsBeforeSpace].Trim();
            remaining = remaining[(equalsBeforeSpace + 1)..].TrimStart();
            
            if (!remaining.StartsWith("\""))
                throw new ArgumentException("Attribute values must be quoted");
                
            var endQuote = remaining.IndexOf("\"", 1);
            if (endQuote == -1)
                throw new ArgumentException("Unterminated attribute value");
                
            var value = remaining[1..endQuote];
            attributes[name] = UnescapeXml(value);
            
            remaining = remaining[(endQuote + 1)..];
        }
    }

    private static string UnescapeXml(string value)
    {
        return System.Net.WebUtility.HtmlDecode(value);
    }

    private static LmmlElement ParseNextElement(ref string remaining)
    {
        // Find the end of this element
        var tagEnd = remaining.IndexOf('>');
        if (tagEnd == -1) throw new ArgumentException("Invalid LMML format");
        
        // Check if it's self-closing
        if (remaining[tagEnd - 1] == '/')
        {
            var parsedElement = Parse(remaining[..(tagEnd + 1)]);
            remaining = remaining[(tagEnd + 1)..];
            return parsedElement;
        }

        // Find the tag name
        var firstSpace = remaining.IndexOf(' ');
        var tagNameEnd = firstSpace == -1 || firstSpace > tagEnd ? tagEnd : firstSpace;
        var tagName = remaining[1..tagNameEnd];

        // Find the closing tag
        var closingTag = $"</{tagName}>";
        var closingTagIndex = remaining.IndexOf(closingTag);
        if (closingTagIndex == -1) throw new ArgumentException($"No closing tag found for {tagName}");

        // Parse this complete element
        var elementStr = remaining[..(closingTagIndex + closingTag.Length)];
        var element = Parse(elementStr);
        
        // Update remaining
        remaining = remaining[(closingTagIndex + closingTag.Length)..];
        
        return element;
    }
}

public abstract class LmmlContent 
{
    public abstract string ToString(int nestingLevel = 0);
    public override string ToString() => ToString(0);
}

public class LmmlStringContent : LmmlContent 
{
    public LmmlStringContent(string content, bool raw = false) { Content = content; Raw = raw; }
    public string Content { get; }
    public bool Raw { get; }
    public override string ToString(int nestingLevel) => Content;
}

public class LmmlChildContent : LmmlContent 
{
    public List<LmmlElement> Children { get; set; } = new();
    public override string ToString(int nestingLevel) 
    {
        var indent = new string(' ', nestingLevel);
        return string.Join("\n" + indent, Children.Select(c => c.ToString(nestingLevel)));
    }
}

public class LmmlRoomEventElement : LmmlTimestampElement
{
    public new Dictionary<string, string> Attributes
    {
        get => base.Attributes;
        set
        {
            var requiredKeys = new[] { "systemId", "roomId", "timestamp" };
            var conflicts = value.Keys.Intersect(requiredKeys);
            if (conflicts.Any())
            {
                throw new ArgumentException(
                    $"Cannot override required attributes in {GetType().Name}: {string.Join(", ", conflicts)}");
            }
            
            // Preserve required attributes while adding new ones
            var required = base.Attributes;
            base.Attributes = value;
            foreach (var kvp in required)
            {
                base.Attributes[kvp.Key] = kvp.Value;
            }
        }
    }

    public required string SystemId
    {
        get => Attributes["systemId"];
        set => Attributes["systemId"] = value;
    }

    public required string RoomId
    {
        get => Attributes["roomId"];
        set => Attributes["roomId"] = value;
    }
    public LmmlRoomEventElement(string tag) : base(tag)
    {
    }
}


public class LmmlTimestampElement : LmmlElement
{
    public new Dictionary<string, string> Attributes
    {
        get => base.Attributes;
        set
        {
            if (value.ContainsKey("timestamp"))
            {
                throw new ArgumentException(
                    $"Cannot override required attribute 'timestamp' in {GetType().Name}");
            }
            
            // Preserve required attributes while adding new ones
            var required = base.Attributes;
            base.Attributes = value;
            foreach (var kvp in required)
            {
                base.Attributes[kvp.Key] = kvp.Value;
            }
        }
    }

    public required DateTimeOffset Timestamp
    {
        get { return DateTimeOffset.Parse(Attributes["timestamp"]); }
        set { Attributes["timestamp"] = value.ToString(Constants.DateTimeFormat); }
    }

    public LmmlTimestampElement(string tag, DateTimeOffset? timestamp = null) : base(tag)
    {
        Timestamp = timestamp ?? DateTimeOffset.Now;
    }
}