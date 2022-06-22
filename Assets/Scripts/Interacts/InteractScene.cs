using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractScene : MonoBehaviour, IInteractable
{
	[SerializeField] private string m_sceneDestination; // TODO: drop-down list in Editor?


	public int Depth { private get; set; }


	private bool m_activated = false;


	public bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_sceneDestination);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (Depth > GameController.ZonesFinishedCount + 1)
		{
			// TODO: indicate w/ visual/audio rather than dialogue?
			GameController.Instance.m_dialogueController.Play(null, Color.white, new DialogueController.Line[] { new DialogueController.Line { m_text = "Locked." } }, interactor.GetComponent<AvatarController>(), null);
		}
		else
		{
			// start animations and wait for trigger to call LoadScene()
			m_activated = true;
			interactor.transform.position = new Vector3(transform.position.x, interactor.transform.position.y, interactor.transform.position.z); // TODO: animate into position
			interactor.GetComponent<Animator>().SetTrigger("despawn");
			GetComponent<Animator>().SetTrigger("activate");
		}
	}

	public void LoadScene()
	{
		if (!m_activated || string.IsNullOrEmpty(m_sceneDestination))
		{
			return;
		}
		GameController.Instance.LoadScene(m_sceneDestination);
	}
}
