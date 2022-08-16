using UnityEngine;


public class Secret : MonoBehaviour
{
	[SerializeField] private float m_viewEdgePct = 0.25f;

	[SerializeField] private int m_index = 0;


	private void OnWillRenderObject()
	{
		// early-out if already found
		if (GameController.SecretFound(m_index))
		{
			return;
		}

		// ignore if room is not open
		if (transform.parent.parent == null) // TODO: better way of determining whether a room has been opened?
		{
			return;
		}

		// ignore if blocked by exterior
		Camera camera = Camera.main;
		if ((camera.cullingMask & GameController.Instance.m_layerExterior) != 0)
		{
			return;
		}

		// ignore until we are well within camera view
		Vector2 screenPos = camera.WorldToViewportPoint(transform.position);
		if (screenPos.x < m_viewEdgePct || screenPos.x > 1.0f - m_viewEdgePct || screenPos.y < m_viewEdgePct || screenPos.y > 1.0f - m_viewEdgePct)
		{
			return;
		}

		GameController.SetSecretFound(m_index);
	}
}
