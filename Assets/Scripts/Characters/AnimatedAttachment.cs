using UnityEngine;


public class AnimatedAttachment : MonoBehaviour
{
	private KinematicCharacter m_character;


	private void Awake()
	{
		m_character = GetComponentInParent<KinematicCharacter>(); // TODO: handle detachment/reattachment?
	}

	// NOTE that we use LateUpdate() since ArmOffset can be set by animation keyframes between Update() and LateUpdate()
	private void LateUpdate()
	{
		transform.localPosition = m_character.ArmOffset;
	}
}
