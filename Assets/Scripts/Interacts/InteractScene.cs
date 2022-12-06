using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	[SerializeField] private string m_sceneDestination; // TODO: drop-down list in Editor?

	[SerializeField] private bool m_isSaveDeletion;

	[SerializeField] private Dialogue m_dialogue;

	public WeightedObject<GameObject>[] m_entryVFX;


	public int Depth { private get; set; }

	public int DestinationIndex => string.IsNullOrEmpty(m_sceneDestination) ? -1 : Enumerable.Range(0, SceneManager.sceneCountInBuildSettings).First(idx => SceneUtility.GetScenePathByBuildIndex(idx).Contains(m_sceneDestination)); // NOTE that we can't use SceneManager.GetSceneBy*() since they only check loaded scenes for some reason; see https://forum.unity.com/threads/get-the-name-of-an-unloaded-scene-from-scenemanager.373969/ // TODO: more robust check? efficiency?


	private bool m_activated = false;

	private KinematicCharacter m_interactorMostRecent;
	private float m_gravityModifierOrig = -1.0f;


	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "UNT0001:Empty Unity message", Justification = "required to force enable/disable checkbox in Inspector")]
	private void Start()
	{
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && (m_isSaveDeletion || !string.IsNullOrEmpty(m_sceneDestination)) && !GameController.Instance.m_dialogueController.IsPlaying;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		m_interactorMostRecent = interactor;
		m_gravityModifierOrig = interactor.gravityModifier;

		if (m_isSaveDeletion || !GameController.Instance.Victory)
		{
			// TODO: visual/audio indicator(s) rather than dialogue for non-Victory refusal?
			GameController.Instance.m_dialogueController.Play(m_dialogue.m_dialogue.RandomWeighted().m_lines, gameObject, interactor.GetComponent<KinematicCharacter>(), expressionSets: m_dialogue.m_expressions);
			return;
		}

		StartAnimation();
	}

	public void StartAnimation()
	{
		// start animations and wait for trigger to call LoadScene()
		m_activated = true;
		Vector2 offsetPos = (Vector2)transform.position + m_interactorMostRecent.gameObject.OriginToCenterY();
		m_interactorMostRecent.Teleport(new(offsetPos.x, offsetPos.y, m_interactorMostRecent.transform.position.z)); // TODO: animate into position?
		m_interactorMostRecent.GetComponent<Animator>().SetTrigger("despawn");
		m_interactorMostRecent.gravityModifier = 0.0f; // due to some doors missing ground underneath them...
		GetComponent<Animator>().SetTrigger("activate");
	}

	public void LoadScene()
	{
		if (!m_activated || !CanInteract(null))
		{
			return;
		}

		if (m_isSaveDeletion)
		{
			GameController.Instance.DeleteSaveAndQuit();
		}
		else
		{
			if (m_interactorMostRecent != null)
			{
				m_interactorMostRecent.gravityModifier = m_gravityModifierOrig; // due to some doors missing ground underneath them...
			}
			GameController.Instance.LoadScene(m_sceneDestination);
		}
	}
}
