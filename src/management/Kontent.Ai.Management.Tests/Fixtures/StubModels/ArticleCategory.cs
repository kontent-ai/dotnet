using Kontent.Ai.Management.Annotations;

namespace Kontent.Ai.Management.Tests.Fixtures.StubModels;

internal enum ArticleCategory
{
    [ContentOption("news", "11111111-aaaa-1111-aaaa-111111111111")]
    News,

    [ContentOption("release", "22222222-aaaa-2222-aaaa-222222222222")]
    Release,
}
