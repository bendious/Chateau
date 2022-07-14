using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	[SerializeField] private string m_sceneDestination; // TODO: drop-down list in Editor?

	[SerializeField] private bool m_isSaveDeletion;


	public int Depth { private get; set; }


	private bool m_activated = false;


	public bool CanInteract(KinematicCharacter interactor) => (m_isSaveDeletion || !string.IsNullOrEmpty(m_sceneDestination)) && !GameController.Instance.m_dialogueController.IsPlaying;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (m_isSaveDeletion)
		{
			GameController.Instance.m_dialogueController.Play(null, Color.white, new DialogueController.Line[] { new DialogueController.Line { m_text = "Leaving so soon? I was hoping you'd stay more like... forever." }, new DialogueController.Line { m_text = "You wouldn't want to lose all your progress, would you?", m_replies = new DialogueController.Line.Reply[] { new DialogueController.Line.Reply { m_text = "No." }, new DialogueController.Line.Reply { m_text = "I guess not." }, new DialogueController.Line.Reply { m_text = "I don't care. I want to start over.", m_userdataObj = this, m_eventName = "ActivateInteract" }, new DialogueController.Line.Reply { m_text = "Fine. I'll stay." } } } }, interactor.GetComponent<AvatarController>(), null); // TODO: un-hardcode?
			return;
		}

		if (Depth > GameController.ZonesFinishedCount + 1)
		{
			// TODO: indicate w/ visual/audio rather than dialogue?
			GameController.Instance.m_dialogueController.Play(null, Color.white, new DialogueController.Line[] { new DialogueController.Line { m_text = "Locked." } }, interactor.GetComponent<AvatarController>(), null);
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
		interactor.transform.position = new Vector3(transform.position.x, interactor.transform.position.y, interactor.transform.position.z); // TODO: animate into position?
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
