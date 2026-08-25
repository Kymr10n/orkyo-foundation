using Api.Constants;
using Api.Services.Ai;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The system prompt is a maintained artifact, not a string constant. These tests pin the
/// clauses that carry security or correctness weight, so an edit that drops one fails
/// here instead of quietly weakening the assistant.
/// </summary>
public class AiSystemPromptTests
{
    [Fact]
    public void StaticPrompt_KeepsEveryGuardrailClause()
    {
        var prompt = AiSystemPrompt.Static();

        foreach (var phrase in AiPromptInvariants.RequiredPhrases)
            prompt.Should().Contain(phrase, $"'{phrase}' is a guardrail the assistant relies on");
    }

    [Fact]
    public void StaticPrompt_DescribesEveryConflictKindTheApplicationCanProduce()
    {
        var prompt = AiSystemPrompt.Static();

        // The taxonomy is what makes conflict guidance useful. A kind added to
        // ConflictKinds without prompt guidance would produce vague advice.
        var kinds = typeof(ConflictKinds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var kind in kinds)
            prompt.Should().Contain(kind, $"conflict kind '{kind}' needs resolution guidance in the prompt");
    }

    [Fact]
    public void StaticPrompt_UsesWorkspaceVocabulary_NotTenant()
    {
        // "tenant" is an implementation word; user-facing text says "workspace".
        AiSystemPrompt.Static().Should().NotContain("the tenant");
    }

    [Fact]
    public void DynamicPrompt_TellsAViewerNotToProposeChanges()
    {
        var prompt = AiSystemPrompt.Dynamic(callerCanEdit: false);

        prompt.Should().Contain("read-only access");
        prompt.Should().Contain("do not call a propose tool");
    }

    [Fact]
    public void DynamicPrompt_TellsAnEditorProposalsAreUseful()
    {
        AiSystemPrompt.Dynamic(callerCanEdit: true).Should().Contain("can edit the schedule");
    }

    [Fact]
    public void ConflictSeed_NamesTheRequestAndAsksForALookUpFirst()
    {
        var requestId = Guid.NewGuid();

        var seed = AiSystemPrompt.ConflictSeed(requestId, ConflictKinds.Overlap);

        seed.Should().Contain(requestId.ToString());
        seed.Should().Contain(ConflictKinds.Overlap);
        seed.Should().Contain("before saying anything about it");
    }

    [Fact]
    public void TheDynamicPromptNamesTheSitesZoneWhenThereIsOne()
    {
        // Sites own their working hours and the zone those hours are written in. Without
        // this the assistant reads "tomorrow morning" as UTC's morning everywhere.
        var prompt = AiSystemPrompt.Dynamic(callerCanEdit: true, siteTimeZone: "Europe/Berlin");

        prompt.Should().Contain("Europe/Berlin");
        prompt.Should().Contain("working hours");
    }

    [Fact]
    public void TheDynamicPromptStaysQuietWhenTheZoneIsUtcOrUnknown()
    {
        // The opening line already says UTC; repeating it adds tokens and no information.
        AiSystemPrompt.Dynamic(callerCanEdit: true, siteTimeZone: "UTC")
            .Should().NotContain("keeps time in");
        AiSystemPrompt.Dynamic(callerCanEdit: true)
            .Should().NotContain("keeps time in");
    }

    [Fact]
    public void TheStaticPromptTeachesNoRetiredVocabulary()
    {
        // The product renamed Spaces to Stations in 0.18.0. Nothing failed when the prompt
        // kept the old words, so it kept them: this is the assertion that was missing.
        var prompt = AiSystemPrompt.Static();

        foreach (var retired in AiPromptInvariants.RetiredPhrases)
        {
            prompt.Should().NotContain(retired,
                $"'{retired}' is vocabulary the product retired — the assistant must speak the words on the person's screen");
        }
    }

    [Fact]
    public void TheStaticPromptExplainsWhatAResourceTypeIs()
    {
        // Types are the workspace's own since 0.18.0. Without this the model assumes the
        // retired built-in trio and invents types that do not exist here.
        var prompt = AiSystemPrompt.Static();

        prompt.Should().Contain("defines its own resource types");
        prompt.Should().Contain("Stations are placed");
    }

    [Fact]
    public void TheStaticPromptDistinguishesARequestFromItsAssignment()
    {
        // The two are not interchangeable: a request is demand, an assignment places it.
        AiSystemPrompt.Static().Should().Contain("are not the same thing");
    }
}
