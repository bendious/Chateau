using System.Collections;
using UnityEngine;


[RequireComponent(typeof(Collider2D))]
public class InteractLookout : MonoBehaviour, IInteractable
{
	[SerializeField]
	private float m_distanceMax = 2.0f; // TODO: tie to max focus distance?


	private bool m_active;


	public void Interact(KinematicCharacter interactor)
	{
		m_active = !m_active;
		if (!m_active)
		{
			return;
		}

		GameController.Instance.AddCameraTargets(GameController.Instance.RoomBackdrops);
		Camera.main.cullingMask |= (LayerMask)GameController.Instance.m_layerExterior; // TODO: fade in
		StartCoroutine(CleanupCoroutine(interactor));
	}


	private IEnumerator CleanupCoroutine(KinematicCharacter interactor)
	{
		yield return new WaitUntil(() => !m_active || Vector2.Distance(interactor.transform.position, transform.position) > m_distanceMax);

		Camera.main.cullingMask &= ~(LayerMask)GameController.Instance.m_layerExterior; // TODO: fade out
		GameController.Instance.RemoveCameraTargets(GameController.Instance.RoomBackdrops);
		m_active = false;
	}
}
