using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	[SerializeField] private string m_sceneDestination; // TODO: drop-down list in Editor?

	[SerializeField] private bool m_isSaveDeletion;


	public int Depth { private get; set; }

	public int DestinationIndex => string.IsNullOrEmpty(m_sceneDestination) ? -1 : Enumerable.Range(0, SceneManager.sceneCountInBuildSettings).First(idx => SceneUtility.GetScenePathByBuildIndex(idx).Contains(m_sceneDestination)); // NOTE that we can't use SceneManager.GetSceneBy*() since they only check loaded scenes for some reason; see https://forum.unity.com/threads/get-the-name-of-an-unloaded-scene-from-scenemanager.373969/ // TODO: more robust check? efficiency?


	private bool m_activated = false;


	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "UNT0001:Empty Unity message", Justification = "required to force enable/disable checkbox in Inspector")]
	private void Start()
	{
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && (m_isSaveDeletion || !string.IsNullOrEmpty(m_sceneDestination)) && !GameController.Instance.m_dialogueController.IsPlaying;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (m_isSaveDeletion)
		{
			GameController.Instance.m_dialogueController.Play(new DialogueController.Line[] { new() { m_text = "Leaving so soon? I was hoping you'd stay more like... forever." }, new() { m_text = "You wouldn't want to lose all your progress, would you?", m_replies = new DialogueController.Line.Reply[] { new() { m_text = "No." }, new() { m_text = "I guess not." }, new() { m_text = "I don't care. I want to start over.", m_userdataObj = this, m_eventName = "ActivateInteract" }, new() { m_text = "Fine. I'll stay." } } } }, interactor.GetComponent<AvatarController>()); // TODO: un-hardcode?
			return;
		}

		if (Depth > GameController.ZonesFinishedCount + 1)
		{
			// TODO: indicate w/ visual/audio rather than dialogue?
			GameController.Instance.m_dialogueController.Play(new DialogueController.Line[] { new() { m_text = "Locked." } }, interactor.GetComponent<AvatarController>());
		}
		else
		{
			StartAnimation(interactor);
		}
	}

	public void StartAnimation(KinematicCharacter interactor)
	{
		// start animations and wait for trigger to call LoadScene()
		m_activated = true;
		interactor.transform.position = new(transform.position.x, interactor.transform.position.y, interactor.transform.position.z); // TODO: animate into position?
		interactor.GetComponent<Animator>().SetTrigger("despawn");
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
			GameController.Instance.LoadScene(m_sceneDestination);
		}
	}
}
