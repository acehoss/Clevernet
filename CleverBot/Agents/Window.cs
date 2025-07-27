using System.Collections.Immutable;
using System.ComponentModel;
using CleverBot.Helpers;

namespace CleverBot.Agents;

/// <summary>
/// Represents a scrollable content window in the agent's interface
/// </summary>
/// <remarks>
/// Windows provide agents with sophisticated content management for:
/// - Viewing file contents with pagination
/// - Browsing web pages
/// - Displaying search results
/// - Managing conversation context
/// Windows support scrolling, maximizing/minimizing, pinning, and auto-refresh
/// </remarks>
public class Window
{
    /// <summary>
    /// Warning message shown when content is truncated
    /// </summary>
    public const string TruncationWarning = "\n\nWARNING: CONTENT TRUNCATED TO FIT CONTEXT WINDOW, SCROLL REQUIRED";
    
    /// <summary>
    /// Renders the window as an LMML element
    /// </summary>
    /// <param name="contentCharLimit">Maximum characters to include in content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>LMML representation of the window</returns>
    public async Task<LmmlElement> Render(int? contentCharLimit, CancellationToken cancellationToken = default)
    {
        if (Refresh != null && AutoRefresh)
            await DoRefresh(cancellationToken);

        var children = new List<LmmlElement>();
        if (Query != null)
            children.Add(new LmmlElement("queryResult")
                { Attributes = new() { ["query"] = Query ?? "" }, Content = new LmmlStringContent(QueryResult ?? "") });
        var contentString = IsMaximized ? Content : GetVisibleContent();
        if (contentCharLimit != null && contentString.Length > contentCharLimit)
            contentString = contentString.Substring(0, contentCharLimit.Value - TruncationWarning.Length) + TruncationWarning;
        var content = new LmmlElement("content") { Content = new LmmlStringContent(contentString, true) };
        children.Add(content);
        
        var childNode = children.Count == 1 ? children[0].Content : new LmmlChildContent() { Children = children };
        
        var el = new LmmlElement("window")
        {
            Attributes = new()
            {
                ["windowId"] = Id.ToString(),
                ["srcType"] = ContentSourceType,
                ["src"] = ContentSource,
                ["contentType"] = ContentType,
                ["lines"] = TotalLines.ToString(),
                ["chars"] = TotalChars.ToString(),
            },
            Content = IsMinimized ? null : childNode
        };

        if (IsMaximized)
        {
            el.Attributes["maximized"] = LmmlElement.BooleanTrueValue;
        }
        else if (IsMinimized)
        {
            el.Attributes["minimized"] = LmmlElement.BooleanTrueValue;
        }

        if (!IsMinimized && !IsMaximized)
        {
            el.Attributes["topLineNumber"] = TopLineNo.ToString();
            el.Attributes["bottomLineNumber"] = BottomLineNo.ToString();
        }

        if (IsSystem)
        {
            el.Attributes["system"] = LmmlElement.BooleanTrueValue;
        }
        
        if(Title != null)
            el.Attributes["title"] = Title;
        if (Refresh != null && AutoRefresh)
            el.Attributes["autorefresh"] = LmmlElement.BooleanTrueValue;
        else if (Refresh != null)
            el.Attributes["refreshable"] = LmmlElement.BooleanTrueValue;

        if (IsPinned)
            el.Attributes["pinned"] = LmmlElement.BooleanTrueValue;
        else if (AutoCloseInTurns > 1)
            el.Attributes["autoCloseInTurns"] = AutoCloseInTurns.ToString();
        else if (AutoCloseInTurns < 1)
            el.Attributes["willAutoCloseAfterTurn"] = LmmlElement.BooleanTrueValue;

        // Merge in custom attributes - they won't overwrite Window's own attributes
        foreach (var attr in _customAttributes)
        {
            if (!el.Attributes.ContainsKey(attr.Key))
                el.Attributes[attr.Key] = attr.Value;
        }
            

        return el;
    }
    private static int NextId = Random.Shared.Next(1000000);
    
    /// <summary>
    /// Unique identifier for this window instance
    /// </summary>
    public int Id { get; } = NextId++;
    
    /// <summary>
    /// Custom attributes that can be added to the window's LMML representation
    /// </summary>
    private readonly Dictionary<string, string> _customAttributes = new();
    
    /// <summary>
    /// Sets a custom attribute on the window
    /// </summary>
    /// <param name="key">Attribute name</param>
    /// <param name="value">Attribute value</param>
    public void SetAttribute(string key, string value)
    {
        _customAttributes[key] = value;
    }
    
    /// <summary>
    /// Removes a custom attribute from the window
    /// </summary>
    /// <param name="key">Attribute name to remove</param>
    public void RemoveAttribute(string key)
    {
        _customAttributes.Remove(key);
    }

    /// <summary>
    /// Clears all custom attributes from the window
    /// </summary>
    public void ClearAttributes()
    {
        _customAttributes.Clear();
    }

    /// <summary>
    /// Function that generates an event when the window is closed
    /// </summary>
    public Func<LmmlTimestampElement>? CloseEvent { get; set; }
    
    /// <summary>
    /// The source of the window's content (e.g., file path, URL)
    /// </summary>
    public string ContentSource { get; set; } = "";
    
    /// <summary>
    /// The type of content source (e.g., "file", "www browser", "search")
    /// </summary>
    public string ContentSourceType { get; set; } = "";
    
    /// <summary>
    /// The actual content displayed in the window
    /// </summary>
    public string Content { get; set; } = "";
    
    /// <summary>
    /// Current query being asked about the window content
    /// </summary>
    public string? Query { get; set; }
    
    /// <summary>
    /// Result of the current query
    /// </summary>
    public string? QueryResult { get; set; }
    
    /// <summary>
    /// Window title displayed in the UI
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// MIME type of the content (e.g., "text/plain", "text/markdown")
    /// </summary>
    public string ContentType { get; set; } = "";
    
    /// <summary>
    /// Function to refresh the window's content
    /// </summary>
    public Func<Window, CancellationToken, Task>? Refresh { get; set; }

    /// <summary>
    /// Refreshes the window content if a refresh function is defined
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task DoRefresh(CancellationToken cancellationToken = default)
    {
        if(Refresh != null)
            await Refresh(this, cancellationToken);
        
        ClampLineNumbers();
    }

    /// <summary>
    /// Ensures line numbers are within valid bounds
    /// </summary>
    public void ClampLineNumbers()
    {
        var totalLines = TotalLines;
        if (totalLines > 0)
        {
            TopLineNo = Math.Max(1, Math.Min(TopLineNo, totalLines));
            BottomLineNo = Math.Max(1, Math.Min(BottomLineNo, totalLines));
        }
        else
        {
            TopLineNo = 1;
            BottomLineNo = 1;
        }
    }

    /// <summary>
    /// Gets the currently visible content based on viewport settings
    /// </summary>
    /// <returns>The content visible in the current viewport</returns>
    public string GetVisibleContent()
    {
        return string.Join("\n", Content.Split("\n").Skip(TopLineNo - 1).Take(BottomLineNo - (TopLineNo - 1)));
    }
    
    /// <summary>
    /// Total number of lines in the content
    /// </summary>
    public int TotalLines => Content.Count(c => c == '\n') + 1;
    
    /// <summary>
    /// Total number of characters in the content
    /// </summary>
    public long TotalChars => Content.Length;
    
    /// <summary>
    /// Maximum number of lines to display when not maximized
    /// </summary>
    public int MaxLines { get; set; } = 60;
    
    /// <summary>
    /// Line number at the top of the viewport (1-based)
    /// </summary>
    public int TopLineNo { get; set; } = 1;
    
    /// <summary>
    /// Line number at the bottom of the viewport (1-based)
    /// </summary>
    public int BottomLineNo { get; set; } = 20;
    
    /// <summary>
    /// Whether the window should automatically refresh its content
    /// </summary>
    public bool AutoRefresh { get; set; } = false;
    
    /// <summary>
    /// Number of lines to scroll when scrolling up/down
    /// </summary>
    public int ScrollSize { get; set; } = 20;
    
    /// <summary>
    /// Whether the window is maximized (shows all content up to MaxLines)
    /// </summary>
    public bool IsMaximized { get; set; } = false;
    
    /// <summary>
    /// Whether the window is minimized (hides content)
    /// </summary>
    public bool IsMinimized { get; set; } = false;
    
    /// <summary>
    /// Whether the window is pinned (prevents auto-close)
    /// </summary>
    public bool IsPinned { get; set; } = false;
    
    /// <summary>
    /// Whether this is a system window (cannot be closed by agent)
    /// </summary>
    public bool IsSystem { get; set; } = false;
    
    /// <summary>
    /// Initial number of turns before auto-close
    /// </summary>
    public int InitialAutoCloseInTurns { get; set; } = 2;
    
    /// <summary>
    /// Number of turns remaining before auto-close
    /// </summary>
    public int AutoCloseInTurns { get; set; } = 2;

    /// <summary>
    /// Scrolls the window up by ScrollSize lines
    /// </summary>
    public void ScrollUp()
    {
        IsMinimized = false;
        AutoCloseInTurns = InitialAutoCloseInTurns;
        TopLineNo -= ScrollSize;
        if(BottomLineNo > TopLineNo + MaxLines)
            BottomLineNo = TopLineNo + MaxLines;
        ClampLineNumbers();
    }

    /// <summary>
    /// Scrolls the window down by ScrollSize lines
    /// </summary>
    public void ScrollDown()
    {
        IsMinimized = false;
        AutoCloseInTurns = InitialAutoCloseInTurns;
        AutoCloseInTurns = InitialAutoCloseInTurns;
        BottomLineNo += ScrollSize;
        if(BottomLineNo > TopLineNo + MaxLines)
            TopLineNo = BottomLineNo - MaxLines;
        ClampLineNumbers();
    }

    /// <summary>
    /// Scrolls the window to a specific line number
    /// </summary>
    /// <param name="lineNo">The line number to scroll to</param>
    public void ScrollToLine(int lineNo)
    {
        IsMinimized = false;
        AutoCloseInTurns = InitialAutoCloseInTurns;
        if(lineNo < 1)
            lineNo = 1;
        if(lineNo + ScrollSize > TotalLines)
            lineNo = TotalLines - ScrollSize;
        
        TopLineNo = lineNo;
        BottomLineNo = lineNo + ScrollSize;
        ClampLineNumbers();
    }

    /// <summary>
    /// Resizes the window viewport to show a specific number of lines
    /// </summary>
    /// <param name="lines">Number of lines to show</param>
    public void Resize(int lines)
    {
        IsMinimized = false;
        AutoCloseInTurns = InitialAutoCloseInTurns;
        if(lines < 1)
            lines = 1;
        if(lines > Math.Min(MaxLines, TotalLines))
            lines = Math.Min(MaxLines, TotalLines);
        BottomLineNo = TopLineNo + lines;
        if(BottomLineNo > TotalLines)
            TopLineNo = BottomLineNo - lines;
        ClampLineNumbers();
    }

    /// <summary>
    /// Maximizes the window to show all content (up to MaxLines)
    /// </summary>
    public void Maximize()
    {
        IsMinimized = false;
        IsMaximized = true;
        AutoCloseInTurns = InitialAutoCloseInTurns;
        ClampLineNumbers();
    }

    /// <summary>
    /// Minimizes the window to hide content
    /// </summary>
    public void Minimize()
    {
        AutoCloseInTurns = InitialAutoCloseInTurns;
        IsMinimized = true;
        IsMaximized = false;
    }
    
    /// <summary>
    /// Restores the window from minimized state
    /// </summary>
    public void Restore()
    {
        AutoCloseInTurns = InitialAutoCloseInTurns;
        IsMinimized = false;
    }

    /// <summary>
    /// Pins the window to prevent auto-close
    /// </summary>
    public void Pin()
    {
        IsPinned = true;
    }

    /// <summary>
    /// Unpins the window to allow auto-close
    /// </summary>
    public void Unpin()
    {
        IsPinned = false;
        AutoCloseInTurns = InitialAutoCloseInTurns;
    }

    /// <summary>
    /// Shows a query and its result in the window
    /// </summary>
    /// <param name="query">The query being asked</param>
    /// <param name="result">The result of the query</param>
    public void ShowQuery(string query, string result)
    {
        AutoCloseInTurns = InitialAutoCloseInTurns;
        Query = query;
        QueryResult = result;
    }

    /// <summary>
    /// Clears any active query from the window
    /// </summary>
    public void ClearQuery()
    {
        AutoCloseInTurns = InitialAutoCloseInTurns;
        Query = null;
        QueryResult = null;
    }
}