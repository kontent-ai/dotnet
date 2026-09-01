using System.Text.Json;
using Kontent.Ai.Delivery.ContentItems.Elements;

namespace Kontent.Ai.Delivery.Tests.ContentItems.Mapping;

public sealed class RichTextElementEnvelopeReaderTests
{
    [Fact]
    public void RichTextElementEnvelopeReader_ParsesImagesLinksAndModularContent()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "type": "rich_text",
              "name": "Body Copy",
              "value": "<p>Hello world</p>",
              "images": {
                "11111111-1111-1111-1111-111111111111": {
                  "description": "Hero",
                  "url": "https://example.com/image.jpg",
                  "height": 100,
                  "width": 200,
                  "image_id": "11111111-1111-1111-1111-111111111111"
                }
              },
              "links": {
                "22222222-2222-2222-2222-222222222222": {
                  "codename": "linked_item",
                  "url_slug": "linked-item",
                  "type": "article"
                }
              },
              "modular_content": ["component_a", "", "linked_item"]
            }
            """);

        var richText = RichTextElementEnvelopeReader.Read(doc.RootElement, "body_copy");

        Assert.Equal("rich_text", richText.Type);
        Assert.Equal("Body Copy", richText.Name);
        Assert.Equal("body_copy", richText.Codename);
        Assert.Equal("<p>Hello world</p>", richText.Value);

        Assert.Single(richText.Images);
        var image = richText.Images.Values.Single();
        Assert.Equal("https://example.com/image.jpg", image.Url);
        Assert.Equal("Hero", image.Description);
        Assert.Equal(100, image.Height);
        Assert.Equal(200, image.Width);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), image.ImageId);

        Assert.Single(richText.Links);
        Assert.Equal(["component_a", "linked_item"], richText.ModularContent);
    }

    [Fact]
    public async Task ParseRichTextAsync_BakesEnvelopeMetadataIntoTheBlocks()
    {
        // The envelope's images and links reach resolution per block, baked in at parse time - the
        // container carries no second copy of them.
        using var doc = JsonDocument.Parse(
            """
            {
              "type": "rich_text",
              "name": "Body Copy",
              "codename": "body_copy",
              "value": "<figure data-asset-id=\"11111111-1111-1111-1111-111111111111\"><img src=\"x\" data-asset-id=\"11111111-1111-1111-1111-111111111111\"></figure><p><a data-item-id=\"22222222-2222-2222-2222-222222222222\">link</a></p>",
              "images": {
                "11111111-1111-1111-1111-111111111111": {
                  "description": "Hero",
                  "url": "https://example.com/image.jpg",
                  "height": 100,
                  "width": 200,
                  "image_id": "11111111-1111-1111-1111-111111111111"
                }
              },
              "links": {
                "22222222-2222-2222-2222-222222222222": {
                  "codename": "linked_item",
                  "url_slug": "linked-item",
                  "type": "article"
                }
              },
              "modular_content": ["component_a", "", "linked_item"]
            }
            """);

        var richText = await doc.RootElement.ParseRichTextAsync();
        Assert.NotNull(richText);

        var image = Assert.Single(richText.GetInlineImages());
        Assert.Equal("https://example.com/image.jpg", image.Url);
        Assert.Equal("Hero", image.Description);

        var link = Assert.Single(richText.GetContentItemLinks());
        Assert.Equal("linked_item", link.Metadata!.Codename);
        Assert.Equal("article", link.Metadata.ContentTypeCodename);
    }

    [Fact]
    public void RichTextElementEnvelopeReader_MatchesPropertyNamesCaseInsensitively()
    {
        // The typed path used to read this envelope with JsonSerializerOptions.Default, so a recased
        // property left InlineImage.Url - a required member - unset and threw, while the dynamic path
        // read the same payload without complaint.
        using var doc = JsonDocument.Parse(
            """
            {
              "type": "rich_text",
              "name": "Body Copy",
              "value": "<p>Hello world</p>",
              "images": {
                "11111111-1111-1111-1111-111111111111": {
                  "Description": "Hero",
                  "URL": "https://example.com/image.jpg",
                  "Image_Id": "11111111-1111-1111-1111-111111111111"
                }
              },
              "links": {
                "22222222-2222-2222-2222-222222222222": {
                  "Codename": "linked_item",
                  "Url_Slug": "linked-item",
                  "Type": "article"
                }
              }
            }
            """);

        var richText = RichTextElementEnvelopeReader.Read(doc.RootElement, "body_copy");

        var image = richText.Images.Values.Single();
        Assert.Equal("https://example.com/image.jpg", image.Url);
        Assert.Equal("Hero", image.Description);

        var link = richText.Links.Values.Single();
        Assert.Equal("linked_item", link.Codename);
        Assert.Equal("article", link.ContentTypeCodename);
    }
}
