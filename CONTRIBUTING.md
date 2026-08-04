# Ways to contribute 
<img align="right" width="100" height="100" src="https://i.imgur.com/PYTV0jP.png">

There are many different ways in which you can contribute. One of the easiest ways is simply to use our software and provide us with your feedback through the right channel. You can also help us improve the open-source projects by submitting pull requests with code and documentation changes.

## Where to get support
Please note that **level of provided support is always determined by the LICENSE** of a given open-source project. Also, always make sure you use the **[latest version](../../releases)** of any given OS project. We can't provide any help for older versions. We don't want to make things complicated so we try to take the same approach in all our repositories. 

### I found a bug in a Kontent.ai open-source project
<img align="right" width="100" height="100" src="https://i.imgur.com/TYIQdpv.png">

Sorry to hear that. Just log a new [GitHub issue](../../issues) and someone will take a look at it. Remember, the more information you provide, the easier it will be to fix the issue. If you feel like it, you can also fix the bug on your own and submit a new pull request.

### I need help with using the projects and/or coding
<img align="right" width="100" height="100" src="https://cdn.sstatic.net/Sites/stackoverflow/company/img/logos/so/so-icon.svg">

To get help with coding and structuring your projects, use [StackOverflow](https://stackoverflow.com/) and tag your questions with [`kontent-ai`](https://stackoverflow.com/questions/tagged/kontent-ai) tag.

Our team members and the community monitor these channels on a regular basis.

### I want to report a security bug
<img align="right" width="100" height="100" src="https://i.imgur.com/z82nnJB.png">

Security issues and bugs should be reported privately, via email, to Kontent.ai Security Team [security@kontent.ai](mailto:security@kontent.ai). For more details, check the [Security policy](SECURITY.md). 

### I have an idea for a new feature (or feedback on existing functionality)
<img align="right" width="100" height="100" src="https://i.imgur.com/rUFkyPy.png">

Everybody loves new features! You can submit a new [feature request](../../issues) or you can code it on your own and [send us a pull request](#submitting-pull-requests). In either case, don't forget to mention what's the use case and what's the expected output.


## Working in this repository

This repository holds five independently versioned products. Each ships its own packages on
its own schedule, so a product consumes its siblings the way an external consumer does —
through `PackageReference`, at the version declared in
[`Directory.Packages.props`](./Directory.Packages.props).

### Two build modes

```sh
dotnet build                                # default: siblings come from nuget.org
dotnet build -p:UseProjectReferences=true   # siblings come from this working tree
```

The default is the mode everything ships from. It compiles each product against the exact
versions its published package declares as its minimum, so an unqualified `dotnet build` or
`dotnet pack` produces the dependency relationship we intend to ship. Packing in source mode
is refused outright, because the generated `.nuspec` would take sibling versions from
`eng/Versions.props` rather than from the declared floors.

Source mode answers the other question — *do the five products at this commit work together?*
CI runs both legs and both block the merge, so you do not need to remember the flag to be
safe. Reach for it when you change a public API another product consumes and want to see the
effect before pushing.

Within a single product every reference is already a `ProjectReference`. Only cross-product
edges are affected by the flag, and there are two: `Kontent.Ai.AspNetCore` → Delivery, and
`Kontent.Ai.ModelGenerator` → Delivery and Management.

### Changing an API that another product consumes

An **additive** change needs nothing special. Both legs stay green: the consumer still
compiles against the old published version *and* against your new source.

A **removal or rename** cannot have both legs green in one PR — the consumer cannot
simultaneously compile against a published version that still has the old API and a source
tree that no longer does. Deprecate first, remove a cycle later:

1. **Add the replacement.** Keep the old member with `[Obsolete]`, bump the dependency
   product. Both legs green. Release it (*Actions → Prepare release*, then *Publish batch*).
2. **Move the consumer.** Raise its floor in `Directory.Packages.props` to the version you
   just published, switch to the new API, bump the consumer. Both legs green. Release it.
3. **Remove the obsolete member** in a later cycle. Both legs green.

Step 2 has to wait for step 1 to actually reach NuGet: a floor pointing at an unreleased
version makes the whole repo unrestorable, including the release that would have published
it. That is why it is a separate PR rather than part of the same release batch.

Raise a floor only when the consuming code genuinely needs the newer API. Floors are meant
to lag — raising one forces every downstream consumer to upgrade too.


## Submitting pull requests
<img align="right" width="100" height="100" src="https://i.imgur.com/aSeiliy.png">

Unless you're fixing a typo, it's usually a good idea to discuss the feature before you submit a pull request with code changes, so let's start with submitting a new [GitHub issue](../../issues) and discussing the whether it fits the vision of a given project.
You might also read these two blogs posts on contributing code: [Open Source Contribution Etiquette](http://tirania.org/blog/archive/2010/Dec-31.html) by Miguel de Icaza and [Don't "Push" Your Pull Requests](https://www.igvita.com/2011/12/19/dont-push-your-pull-requests/) by Ilya Grigorik. Note that all code submissions will be rigorously reviewed and tested by Kontent.ai maintainer teams, and only those that meet an high bar for both quality and design/roadmap appropriateness will be merged into the source.


### Example - process of contribution
If not stated otherwise, we use [feature branch workflow](https://www.atlassian.com/git/tutorials/comparing-workflows/feature-branch-workflow). 

To start with coding, fork the repository you want to contribute to, create a new branch, and start coding. Once the functionality is [done](#Definition-of-Done), you can submit a [pull request](https://help.github.com/articles/about-pull-requests/). 

### Definition of Done
<img align="right" width="100" height="100" src="https://i.imgur.com/g82Ohdv.png">

- New/fixed code is covered with tests
- CI can build the code
- All tests are pass
- New version number follows [semantic versioning](https://semver.org/)
- Coding style (spaces, indentation) is in line with the rest of the code in a given repository
- Documentation is updated (e.g. code examples in README, Wiki pages, etc.)
- All `public` members are documented (using XML doc, phpdoc, etc.)
- Code doesn't contain any secrets (private keys, etc.)
- Commit messages are clear. Please read these articles: [Writing good commit messages](https://github.com/erlang/otp/wiki/Writing-good-commit-messages), [A Note About Git Commit Messages](https://tbaggery.com/2008/04/19/a-note-about-git-commit-messages.html), [On commit messages](https://who-t.blogspot.com/2009/12/on-commit-messages.html)


### Feedback
<img align="right" width="100" height="100" src="https://i.imgur.com/ZQfNzJJ.png">

Your pull request will now go through extensive checks by the subject matter experts on our team. Please be patient. Update your pull request according to feedback until it is approved by one of Kontent.ai maintainers. After that, one of our team members may adjust the branch you merge into based on the expected release schedule.


## Code of Conduct
<img align="right" width="100" height="100" src="https://i.imgur.com/cObdKQy.png">

The Kontent.ai team is committed to fostering a welcoming community, therefore this project has adopted the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). If you have any additional questions or comments, you can contact us directly at devrel@kontent.ai.
