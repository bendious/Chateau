using System.Collections;
using UnityEngine;


[RequireComponent(typeof(Collider2D))]
public class InteractLookout : MonoBehaviour, IInteractable
{
	[SerializeField] private float m_distanceMax = 2.0f; // TODO: tie to max focus distance?


	private bool m_active;


	public bool CanInteract(KinematicCharacter interactor) => !GameController.Instance.ActiveEnemiesRemain();

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		m_active = !m_active;
		if (!m_active)
		{
			return;
		}

		GameController.Instance.m_vCamOverview.gameObject.SetActive(true);
		Camera.main.cullingMask |= GameController.Instance.m_layerExterior; // TODO: fade in
		StartCoroutine(CleanupCoroutine(interactor));
	}


	private IEnumerator CleanupCoroutine(KinematicCharacter interactor)
	{
		yield return new WaitUntil(() => !m_active || Vector2.Distance(interactor.transform.position, transform.position) > m_distanceMax); // TODO: remain in overview if near ANY InteractLookout?

		Camera.main.cullingMask &= ~GameController.Instance.m_layerExterior; // TODO: fade out
		GameController.Instance.m_vCamOverview.gameObject.SetActive(false);
		m_active = false;
	}
}
