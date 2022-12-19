using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractNpc : MonoBehaviour, IInteractable
{
	public Sprite m_dialogueSprite;

	[SerializeField] private WeightedObject<AudioClip>[] m_sfx;

	[SerializeField] private float m_weightUseScalar = 0.25f;


	public int Index { get; set; }


	private bool HasSingleUseAvailable => DialogueFiltered(false).Any(line => line.m_object.m_singleUse);


	private AIController m_ai;

	private AudioClip m_sfxChosen;

	private WeightedObject<Dialogue.Info>[] m_dialogueCombined;
	private WeightedObject<Dialogue.Expression>[] m_expressionsCombined;


	private void Start()
	{
		m_ai = GetComponent<AIController>();

		m_sfxChosen = m_sfx.RandomWeighted(); // TODO: save/load?

		// set NPC appearance
		// TODO: move elsewhere?
		GetComponent<SpriteRenderer>().color = GameController.NpcColor(Index);

		if (m_ai != null && m_ai.m_friendly && UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 0 && HasSingleUseAvailable) // TODO: remove 0th scene hardcoding?
		{
			m_ai.SetOnlyPursueAvatar(true);
		}
	}


	public bool CanInteract(KinematicCharacter interactor) => !GameController.Instance.m_dialogueController.IsPlaying && !GameController.Instance.ActiveEnemiesRemain();

	public void Interact(KinematicCharacter interactor, bool reverse) => StartCoroutine(PlayDialogueCoroutine(interactor));

	// called via Interact()/SendMessage(NpcDialogue.Info.m_preconditionName)
	public void HasFinishedZone(SendMessageValue<Dialogue.Info, bool> info)
	{
		info.m_out = GameController.ZonesFinishedCount >= info.m_in.m_userdata;
	}

	// called via Interact()/SendMessage(NpcDialogue.Info.m_preconditionName)
	public void HasNotFinishedZone(SendMessageValue<Dialogue.Info, bool> info)
	{
		info.m_out = GameController.ZonesFinishedCount < info.m_in.m_userdata;
	}


#if DEBUG
	public void DebugReset()
	{
		m_dialogueCombined = null;
		m_expressionsCombined = null;
	}
#endif


	private IEnumerable<WeightedObject<Dialogue.Info>> DialogueFiltered(bool excludeReplies, int targetIndex = -1, params Dialogue[] targets)
	{
		// lazy initialize dialogue options
		if (m_dialogueCombined == null)
		{
			InitializeDialogue();
		}

		// filter dialogue options
		float relationshipCur = GameController.RelationshipPercent(Index, targetIndex >= 0 ? targetIndex : Index); // NOTE the use of -1 for "self-relationships" for non-NPC-targeted lines // TODO: better flag value(s)?
		return m_dialogueCombined.Where(info =>
		{
			if (info.m_weight < 0.0f || (excludeReplies && info.m_object.m_lines.Any(line => line.m_replies.Length > 0)) || ((targets != null && targets.Length > 0) ? targets.All(target => target != info.m_object.m_target) : (info.m_object.m_target != null)))
			{
				return false;
			}

			if (relationshipCur < info.m_object.m_relationshipMin || relationshipCur > info.m_object.m_relationshipMax)
			{
				return false;
			}

			if (string.IsNullOrEmpty(info.m_object.m_preconditionName))
			{
				return true;
			}
			SendMessageValue<Dialogue.Info, bool> inOutValues = new() { m_in = info.m_object };
			SendMessage(info.m_object.m_preconditionName, inOutValues);
			return inOutValues.m_out;
		});
	}

	private void InitializeDialogue()
	{
		Dialogue[] dialogue = m_ai.m_dialogues.Concat(GameController.NpcDialogues(Index)).ToArray();
		m_dialogueCombined = dialogue.SelectMany(source => source.m_dialogue).ToArray(); // NOTE the lack of deep-copying here, allowing the source NpcDialogue weights to be edited below and subsequently saved by GameController.Save() // TODO: avoid relying on runtime edits to ScriptableObject?
		m_expressionsCombined = dialogue.SelectMany(source => source.m_expressions).ToArray(); // NOTE the lack of deep-copying here since these shouldn't be edited anyway
	}

	private System.Collections.IEnumerator PlayDialogueCoroutine(KinematicCharacter interactor)
	{
		// pick dialogue option(s)
		InteractNpc otherNpc = interactor.GetComponent<InteractNpc>();
		int targetIndex = otherNpc != null ? otherNpc.Index : Index; // NOTE the use of "self-relationships" for non-NPC-targeted lines // TODO?
		WeightedObject<Dialogue.Info>[] dialogueAllowed = DialogueFiltered(interactor is not AvatarController, targetIndex, otherNpc == null ? interactor.m_dialogues : interactor.m_dialogues.Concat(GameController.NpcDialogues(otherNpc.Index)).ToArray()).ToArray();
		if (dialogueAllowed.Length <= 0)
		{
			yield break;
		}
		Dialogue.Info dialogueCur = dialogueAllowed.FirstOrDefault(dialogue => dialogue.m_weight == 0.0f)?.m_object;
		if (dialogueCur == null)
		{
			dialogueCur = dialogueAllowed.RandomWeighted();
		}
		Dialogue.Info dialogueAppend = dialogueCur.m_singleUse ? null : dialogueAllowed.FirstOrDefault(dialogueWeighted => dialogueWeighted.m_object.m_appendToAll && dialogueWeighted.m_object != dialogueCur)?.m_object; // TODO: support multiple append dialogues?

		// play dialogue and wait
		if (otherNpc != null && otherNpc.m_expressionsCombined == null)
		{
			otherNpc.InitializeDialogue();
		}
		yield return GameController.Instance.m_dialogueController.Play(dialogueAppend != null ? dialogueCur.m_lines.Concat(dialogueAppend.m_lines) : dialogueCur.m_lines, gameObject, interactor, m_ai, m_dialogueSprite, GetComponent<SpriteRenderer>().color, m_sfxChosen, dialogueCur.m_loop ? 0 : dialogueAppend != null && dialogueAppend.m_loop ? dialogueCur.m_lines.Length : -1, expressionSets: new WeightedObject<Dialogue.Expression>[][] { m_expressionsCombined, otherNpc == null ? interactor.m_dialogues.SelectMany(d => d.m_expressions).ToArray() : otherNpc.m_expressionsCombined });

		// update weight
		WeightedObject<Dialogue.Info> weightedDialogueCur = m_dialogueCombined.First(dialogue => dialogue.m_object == dialogueCur); // TODO: support duplicate dialogue options?
		weightedDialogueCur.m_weight = weightedDialogueCur.m_object.m_singleUse ? -1.0f : weightedDialogueCur.m_weight == 0.0f ? 1.0f : weightedDialogueCur.m_weight * m_weightUseScalar;

		// update relationship level
		if (weightedDialogueCur.m_object.m_relationshipIncrement != 0.0f)
		{
			GameController.RelationshipIncrement(Index, targetIndex, weightedDialogueCur.m_object.m_relationshipIncrement);
		}

		// deactivate single-minded pursuit if appropriate
		if (m_ai != null && m_ai.m_friendly && m_ai.OnlyPursueAvatar && !HasSingleUseAvailable)
		{
			m_ai.SetOnlyPursueAvatar(false);
		}
	}
}
