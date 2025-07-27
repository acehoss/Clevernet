using NUnit.Framework;

namespace Clevernet.Tests;

[TestFixture]
public class LmmlTests
{
    [Test]
    public void LmmlFormatTest()
    {
        var lmml = new LmmlElement("test")
        {
            Attributes = new()
            {
                ["test1"] = "test2",
                ["test3"] = "test4"
            },
            Content = new LmmlChildContent
            {
                Children = new()
                {
                    new LmmlElement("test5"),
                    new LmmlElement("test6") { Content = new LmmlStringContent("test7") },
                    new LmmlElement("test8")
                    {
                        Content = new LmmlChildContent
                        {
                            Children = new()
                            {
                                new LmmlElement("test9")
                                {
                                    Attributes = new()
                                    {
                                        ["test10"] = "test11"
                                    },
                                    Content = new LmmlStringContent("test12")
                                }
                            }
                        }
                    },
                }
            }
        };
        
        var result = lmml.ToString();
        Assert.That(result, Is.EqualTo(@"<test test1=""test2"" test3=""test4"">
 <test5/>
 <test6>test7</test6>
 <test8>
  <test9 test10=""test11"">test12</test9>
 </test8>
</test>"));
    }

    [Test]
    public void Parse_SelfClosingTag_NoAttributes()
    {
        var lmml = "<test/>";
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("test"));
        Assert.That(element.Attributes, Is.Empty);
        Assert.That(element.Content, Is.Null);
    }
    
    [Test]
    public void Parse_SelfClosingTag_WithAttributes()
    {
        var lmml = "<test name=\"value\" id=\"123\"/>";
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("test"));
        Assert.That(element.Attributes.Count, Is.EqualTo(2));
        Assert.That(element.Attributes["name"], Is.EqualTo("value"));
        Assert.That(element.Attributes["id"], Is.EqualTo("123"));
        Assert.That(element.Content, Is.Null);
    }
    
    [Test]
    public void Parse_SelfClosingTag_WithBooleanAttributes()
    {
        var lmml = "<test required enabled/>";
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("test"));
        Assert.That(element.Attributes.Count, Is.EqualTo(2));
        Assert.That(element.Attributes["required"], Is.EqualTo(LmmlElement.BooleanTrueValue));
        Assert.That(element.Attributes["enabled"], Is.EqualTo(LmmlElement.BooleanTrueValue));
        Assert.That(element.Content, Is.Null);
    }
    
    [Test]
    public void Parse_ElementWithStringContent()
    {
        var lmml = "<test>Hello, World!</test>";
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("test"));
        Assert.That(element.Attributes, Is.Empty);
        Assert.That(element.Content, Is.TypeOf<LmmlStringContent>());
        Assert.That(((LmmlStringContent)element.Content).Content, Is.EqualTo("Hello, World!"));
    }
    
    [Test]
    public void Parse_ElementWithChildElements()
    {
        var lmml = "<parent><child1/><child2 attr=\"value\"/></parent>";
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("parent"));
        Assert.That(element.Attributes, Is.Empty);
        Assert.That(element.Content, Is.TypeOf<LmmlChildContent>());
        
        var children = ((LmmlChildContent)element.Content).Children;
        Assert.That(children.Count, Is.EqualTo(2));
        
        Assert.That(children[0].Tag, Is.EqualTo("child1"));
        Assert.That(children[0].Attributes, Is.Empty);
        
        Assert.That(children[1].Tag, Is.EqualTo("child2"));
        Assert.That(children[1].Attributes.Count, Is.EqualTo(1));
        Assert.That(children[1].Attributes["attr"], Is.EqualTo("value"));
    }
    
    [Test]
    public void Parse_ComplexElement()
    {
        var lmml = "<root id=\"1\" enabled>\n" +
                   "  <child1>Text content</child1>\n" +
                   "  <child2 type=\"special\"/>\n" +
                   "</root>";
        
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("root"));
        Assert.That(element.Attributes.Count, Is.EqualTo(2));
        Assert.That(element.Attributes["id"], Is.EqualTo("1"));
        Assert.That(element.Attributes["enabled"], Is.EqualTo(LmmlElement.BooleanTrueValue));
        
        Assert.That(element.Content, Is.TypeOf<LmmlChildContent>());
        var children = ((LmmlChildContent)element.Content).Children;
        Assert.That(children.Count, Is.EqualTo(2));
        
        Assert.That(children[0].Tag, Is.EqualTo("child1"));
        Assert.That(children[0].Content, Is.TypeOf<LmmlStringContent>());
        Assert.That(((LmmlStringContent)children[0].Content).Content, Is.EqualTo("Text content"));
        
        Assert.That(children[1].Tag, Is.EqualTo("child2"));
        Assert.That(children[1].Attributes.Count, Is.EqualTo(1));
        Assert.That(children[1].Attributes["type"], Is.EqualTo("special"));
    }
    
    [Test]
    public void Parse_WithEscapedCharacters()
    {
        var lmml = "<test attr=\"&lt;escaped&gt;\" special=\"&quot;quoted&quot;\">Content with &amp;</test>";
        var element = LmmlElement.Parse(lmml);
        
        Assert.That(element.Tag, Is.EqualTo("test"));
        Assert.That(element.Attributes.Count, Is.EqualTo(2));
        Assert.That(element.Attributes["attr"], Is.EqualTo("<escaped>"));
        Assert.That(element.Attributes["special"], Is.EqualTo("\"quoted\""));
        Assert.That(element.Content, Is.TypeOf<LmmlStringContent>());
        Assert.That(((LmmlStringContent)element.Content).Content, Is.EqualTo("Content with &"));
    }
    
    [TestCase("test")]  // No brackets
    [TestCase("<test")] // No closing bracket
    [TestCase("<test>")]  // No closing tag
    [TestCase("<test></wrong>")]  // Mismatched tags
    public void Parse_InvalidLmml_ThrowsArgumentException(string lmml)
    {
        Assert.That(() => LmmlElement.Parse(lmml), Throws.ArgumentException);
    }
    
    [Test]
    public void ToString_MatchesOriginalLmml()
    {
        var original = "<root id=\"1\" enabled>\n" +
                      "  <child1>Text content</child1>\n" +
                      "  <child2 type=\"special\"/>\n" +
                      "</root>";
        
        var element = LmmlElement.Parse(original);
        var regenerated = element.ToString();
        
        // Parse both to normalize whitespace and compare structure
        var originalElement = LmmlElement.Parse(original);
        var regeneratedElement = LmmlElement.Parse(regenerated);
        
        Assert.That(regeneratedElement.Tag, Is.EqualTo(originalElement.Tag));
        Assert.That(regeneratedElement.Attributes, Is.EqualTo(originalElement.Attributes));
        
        if (originalElement.Content is LmmlChildContent originalChildren && 
            regeneratedElement.Content is LmmlChildContent regeneratedChildren)
        {
            Assert.That(regeneratedChildren.Children.Count, Is.EqualTo(originalChildren.Children.Count));
            for (var i = 0; i < originalChildren.Children.Count; i++)
            {
                Assert.That(regeneratedChildren.Children[i].Tag, Is.EqualTo(originalChildren.Children[i].Tag));
                Assert.That(regeneratedChildren.Children[i].Attributes, Is.EqualTo(originalChildren.Children[i].Attributes));
            }
        }
        else if (originalElement.Content is LmmlStringContent originalString && 
                regeneratedElement.Content is LmmlStringContent regeneratedString)
        {
            Assert.That(regeneratedString.Content, Is.EqualTo(originalString.Content));
        }
        else
        {
            Assert.That(regeneratedElement.Content, Is.EqualTo(originalElement.Content));
        }
    }

    [Test]
    public void TimestampElement_ProtectsTimestampAttribute()
    {
        var element = new LmmlTimestampElement("event")
        {
            Timestamp = DateTimeOffset.Now
        };
        var timestamp = element.Timestamp;

        // Should throw when trying to override timestamp
        Assert.Throws<ArgumentException>(() =>
        {
            element.Attributes = new() { ["timestamp"] = "2024-01-01" };
        });

        // Original timestamp should be preserved
        Assert.That(element.Timestamp, Is.EqualTo(timestamp));

        // Should allow setting other attributes
        element.Attributes = new() { ["foo"] = "bar" };
        Assert.That(element.Attributes["foo"], Is.EqualTo("bar"));
        Assert.That(element.Attributes["timestamp"], Is.Not.Null);
    }

    [Test]
    public void RoomEventElement_ProtectsRequiredAttributes()
    {
        var element = new LmmlRoomEventElement("event")
        {
            SystemId = "matrix",
            RoomId = "!room:matrix.org",
            Timestamp = DateTimeOffset.Now
        };
        var timestamp = element.Timestamp;

        // Should throw when trying to override any required attribute
        Assert.Throws<ArgumentException>(() =>
        {
            element.Attributes = new() { ["systemId"] = "discord" };
        });

        Assert.Throws<ArgumentException>(() =>
        {
            element.Attributes = new() { ["roomId"] = "different-room" };
        });

        Assert.Throws<ArgumentException>(() =>
        {
            element.Attributes = new() { ["timestamp"] = "2024-01-01" };
        });

        // Original values should be preserved
        Assert.That(element.SystemId, Is.EqualTo("matrix"));
        Assert.That(element.RoomId, Is.EqualTo("!room:matrix.org"));
        Assert.That(element.Timestamp, Is.EqualTo(timestamp));

        // Should allow setting other attributes
        element.Attributes = new() { ["foo"] = "bar" };
        Assert.That(element.Attributes["foo"], Is.EqualTo("bar"));
        Assert.That(element.SystemId, Is.EqualTo("matrix"));
        Assert.That(element.RoomId, Is.EqualTo("!room:matrix.org"));
        Assert.That(element.Timestamp, Is.EqualTo(timestamp));
    }

    [Test]
    public void StringContent_HandlesRawAndEscapedContent()
    {
        // Test with normal (escaped) content
        var normalElement = new LmmlElement("wrapper")
        {
            Content = new LmmlStringContent("<child>Some text & symbols</child>")
        };
        Assert.That(normalElement.ToString(), Is.EqualTo("<wrapper>&lt;child&gt;Some text &amp; symbols&lt;/child&gt;</wrapper>"));

        // Test with raw (unescaped) content
        var rawElement = new LmmlElement("wrapper")
        {
            Content = new LmmlStringContent("<child>Some text & symbols</child>", raw: true)
        };
        Assert.That(rawElement.ToString(), Is.EqualTo("<wrapper><child>Some text & symbols</child></wrapper>"));

        // Test with nested LMML that's already been rendered
        var nestedLmml = new LmmlElement("child")
        {
            Content = new LmmlStringContent("Some text & symbols")
        }.ToString();
        
        var containerElement = new LmmlElement("wrapper")
        {
            Content = new LmmlStringContent(nestedLmml, raw: true)
        };
        Assert.That(containerElement.ToString(), Is.EqualTo("<wrapper><child>Some text &amp; symbols</child></wrapper>"));
    }
}