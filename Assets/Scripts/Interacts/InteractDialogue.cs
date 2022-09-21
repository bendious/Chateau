using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractDialogue : MonoBehaviour, IInteractable
{
	public Sprite m_dialogueSprite;

	[SerializeField] private WeightedObject<AudioClip>[] m_sfx;

	[SerializeField] private float m_weightUseScalar = 0.25f;


	public int Index { get; set; }


	private bool HasSingleUseAvailable => DialogueFiltered().Any(line => line.m_object.m_singleUse);


	private bool m_isVice;

	private AIController m_ai;

	private AudioClip m_sfxChosen;

	private WeightedObject<NpcDialogue.DialogueInfo>[] m_dialogueCombined;
	private WeightedObject<NpcDialogue.ExpressionInfo>[] m_expressionsCombined;


	private void Start()
	{
		m_isVice = Random.value > 0.5f; // TODO: choose exactly one NPC

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

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		WeightedObject<NpcDialogue.DialogueInfo>[] dialogueAllowed = DialogueFiltered().ToArray();

		// pick dialogue option
		NpcDialogue.DialogueInfo dialogueCur = dialogueAllowed.FirstOrDefault(dialogue => dialogue.m_weight == 0.0f)?.m_object;
		if (dialogueCur == null)
		{
			dialogueCur = dialogueAllowed.RandomWeighted();
		}

		// play dialogue
		GameController.Instance.m_dialogueController.Play(dialogueCur.m_lines, interactor.GetComponent<AvatarController>(), m_dialogueSprite, GetComponent<SpriteRenderer>().color, m_expressionsCombined, m_sfxChosen, dialogueCur.m_loop);

		// update weight
		// TODO: save across instantiations/sessions?
		WeightedObject<NpcDialogue.DialogueInfo> weightedDialogueCur = m_dialogueCombined.First(dialogue => dialogue.m_object == dialogueCur); // TODO: support duplicate dialogue options?
		weightedDialogueCur.m_weight = weightedDialogueCur.m_object.m_singleUse ? -1.0f : weightedDialogueCur.m_weight == 0.0f ? 1.0f : weightedDialogueCur.m_weight * m_weightUseScalar;

		// deactivate single-minded pursuit if appropriate
		if (m_ai != null && m_ai.m_friendly && m_ai.OnlyPursueAvatar && !HasSingleUseAvailable)
		{
			m_ai.SetOnlyPursueAvatar(false);
		}
	}

	// called via Interact()/SendMessage(NpcDialogue.Info.m_preconditionName)
	public void HasFinishedZone(SendMessageValue<NpcDialogue.DialogueInfo, bool> info)
	{
		info.m_out = GameController.ZonesFinishedCount >= info.m_in.m_userdata;
	}

	// called via Interact()/SendMessage(NpcDialogue.Info.m_preconditionName)
	public void HasNotFinishedZone(SendMessageValue<NpcDialogue.DialogueInfo, bool> info)
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


	private System.Collections.Generic.IEnumerable<WeightedObject<NpcDialogue.DialogueInfo>> DialogueFiltered()
	{
		// lazy initialize dialogue options
		if (m_dialogueCombined == null)
		{
			NpcDialogue[] dialogue = GameController.NpcDialogues(Index);
			m_dialogueCombined = dialogue.SelectMany(source => source.m_dialogue).ToArray(); // NOTE the lack of deep-copying here, allowing the source NpcDialogue weights to be edited below and subsequently saved by GameController.Save() // TODO: avoid relying on runtime edits to ScriptableObject?
			m_expressionsCombined = dialogue.SelectMany(source => source.m_expressions).ToArray(); // NOTE the lack of deep-copying here since these shouldn't be edited anyway
		}

		// filter dialogue options
		return m_dialogueCombined.Where(info =>
		{
			if (info.m_weight < 0.0f)
			{
				return false;
			}

			if (string.IsNullOrEmpty(info.m_object.m_preconditionName))
			{
				return true;
			}
			SendMessageValue<NpcDialogue.DialogueInfo, bool> inOutValues = new() { m_in = info.m_object };
			SendMessage(info.m_object.m_preconditionName, inOutValues);
			return inOutValues.m_out;
		});
	}
}
