# Administration

Environment-level configuration, workflow definitions, and the subscription-scoped endpoints.

- [Environment resources](#environment-resources)
- [Languages and workflow definitions](#languages-and-workflow-definitions)
- [Subscription-scoped operations](#subscription-scoped-operations)

These follow the same result and identifier conventions as everything else — see
[requests and results](requests-and-results.md). The full request and response shapes are in the
[Management API reference](https://kontent.ai/learn/docs/apis/openapi/management-api-v2/).

## Environment resources

| Area | Methods |
|------|---------|
| **Languages** | `ListLanguagesAsync`, `GetLanguageAsync`, `CreateLanguageAsync`, `ModifyLanguageAsync` |
| **Workflows** | `ListWorkflowsAsync`, `CreateWorkflowAsync`, `UpdateWorkflowAsync`, `DeleteWorkflowAsync` |
| **Collections** | `GetCollectionsAsync`, `ModifyCollectionsAsync` |
| **Spaces** | `ListSpacesAsync`, `GetSpaceAsync`, `CreateSpaceAsync`, `ModifySpaceAsync`, `DeleteSpaceAsync` |
| **Webhooks** | `ListWebhooksAsync`, `GetWebhookAsync`, `CreateWebhookAsync`, `EnableWebhookAsync`, `DisableWebhookAsync`, `DeleteWebhookAsync` |
| **Preview** | `GetPreviewConfigurationAsync`, `UpdatePreviewConfigurationAsync` |
| **Custom apps** | `ListCustomAppsAsync`, `GetCustomAppAsync`, `CreateCustomAppAsync`, `ModifyCustomAppAsync`, `DeleteCustomAppAsync` |
| **Roles** | `ListEnvironmentRolesAsync`, `GetEnvironmentRoleAsync` |
| **Environment users** | `InviteUserIntoEnvironmentAsync`, `UpdateUserRolesAsync` |
| **Environment lifecycle** | `GetEnvironmentInformationAsync`, `CloneEnvironmentAsync`, `GetEnvironmentCloningStateAsync`, `MarkEnvironmentAsProductionAsync`, `ModifyEnvironmentAsync`, `DeleteEnvironmentAsync` |
| **Validation** | `ValidateEnvironmentAsync`, `InitiateEnvironmentAsyncValidationTaskAsync`, `GetAsyncValidationTaskAsync`, `ListAsyncValidationTaskIssuesAsync` |

Languages, spaces and custom apps are modified with the patch factories described in
[content model](content-model.md#patch-definitions) — `LanguagePatch`, `SpacePatch`, `CustomAppPatch` —
which name the property and take a correctly-typed value:

```csharp
await client.ModifyLanguageAsync(Reference.ByCodename("de-DE"),
[
    LanguagePatch.Name("Deutsch"),
    LanguagePatch.FallbackLanguage(Reference.ByCodename("en-US")),
]);

await client.ModifySpaceAsync(Reference.ByCodename("marketing"), [SpacePatch.RootItem(null)]);   // null unsets
```

An async validation task's issues can be numerous, so that listing has a page overload — see
[pagination](requests-and-results.md#pagination).

## Languages and workflow definitions

Creating a language:

```csharp
await client.CreateLanguageAsync(new LanguageCreateModel
{
    Name = "German",
    Codename = "de-DE",
    IsActive = true,
    FallbackLanguage = Reference.ByCodename("en-US")
});
```

A **workflow** here is the definition — its steps and the scopes it applies to. Moving a variant
*through* a workflow is a content operation, in
[content items and variants](content-items-and-variants.md#publish-schedule-and-change-workflow-state).

## Subscription-scoped operations

These resolve against `/v2/subscriptions/{id}` rather than an environment:

`ListSubscriptionProjectsAsync`, `ListSubscriptionUsersAsync`, `GetSubscriptionUserAsync`,
`ActivateSubscriptionUserAsync`, `DeactivateSubscriptionUserAsync`.

> [!IMPORTANT]
> They take a **Subscription API key**, not the Management API key an environment call uses. It is
> minted at `https://app.kontent.ai/subscription/<subscription-id>/api-keys`, only a subscription admin
> can create one, and an environment's Management API key will not authenticate these endpoints.

They need `SubscriptionId` set; calling one without it throws `InvalidOperationException` naming the
missing option. A client that only calls them needs no `EnvironmentId`:

```csharp
await using var client = new ManagementClient(new ManagementOptions
{
    ApiKey = "<your Subscription API key>",
    SubscriptionId = "<your subscription id>",
});

var projects = (await client.ListSubscriptionProjectsAsync()).EnsureSuccess();
```

A client has one `ApiKey`. If you need both scopes, register two named clients — see
[named clients](configuration.md#named-clients).

Users are addressed with `UserIdentifier` rather than `Reference` — see
[identifiers](requests-and-results.md#identifiers).
