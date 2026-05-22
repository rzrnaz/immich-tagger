# PhotoAIApp Prompt Quality Tuning

This update tightens the Ollama vision prompt based on the first Immich ingestion review.

## Observed issue

One successful test imported this description and tags into Immich:

- Description: `A person sits on a boat while the canopy is down.`
- Tags included: `outdoor`, `people`, `person`, `relaxation`, `boat`

The metadata plumbing worked: Immich imported the sentence description separately from tags. The quality issue was model wording/tag choice:

- `canopy is down` inferred a mechanical state that was not visually safe.
- `relaxation` was plausibly true, but it is a mood/activity tag and less useful than concrete visual subjects.
- Both `people` and `person` can be redundant when only one person is visible.

## Prompt changes

The prompt now tells the model to:

- Describe only what is visibly supported by the image.
- Avoid inferring mechanical state, intent, or cause.
- Avoid phrases such as `canopy is up/down`, `parked`, `broken`, `waiting`, `relaxing`, `posing`, or `celebrating` unless visually unambiguous.
- Use conservative wording for people, relationships, roles, and counts.
- Prefer one concise factual sentence for the description.
- Prefer 3 to 7 tags.
- Prefer concrete visible subjects/settings.
- Avoid mood/activity tags such as `relaxation`, `leisure`, `happiness`, `vacation`, `event`, `lifestyle`, `travel`, and `fun` unless strongly supported.
- Include tags only when useful for finding the image later.

## Recommended assessment rubric for the 10-image batch

For each image, score:

1. Description accuracy
   - Is every claim visibly supported?
   - Does it avoid mechanical-state guesses like canopy up/down?
   - Does it avoid identity/relationship/location guesses?

2. Description usefulness
   - Is it a concise sentence someone would want in Immich?
   - Does it mention the main subject and setting?
   - Does it avoid overexplaining background clutter?

3. Tag usefulness
   - Are tags concrete searchable nouns/settings?
   - Are there 3 to 7 useful tags?
   - Are mood/activity tags omitted unless obvious?
   - Are redundant pairs like `person` + `people` minimized?

4. Quality flags
   - Only flags real technical issues: blurry, dark, overexposed, screenshot, document, duplicate-looking, none.

## Files changed

- `PhotoAIApp.Core/PhotoAiScanner.cs`
  - Updated model prompt.
- `tests/test_prompt_quality_rules.py`
  - Added source-level regression tests for prompt guidance.

## Verification

```text
python3 -m pytest tests/test_prompt_quality_rules.py tests/test_xmp_add_tags_behavior.py -q
6 passed
```
