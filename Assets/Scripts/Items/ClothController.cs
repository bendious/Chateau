using UnityEngine;


public class ClothController : MonoBehaviour
{
	[SerializeField] private Vector3 m_offset;
	[SerializeField] private bool m_offsetFromHead;


	private KinematicCharacter m_character;

	private bool m_wasLeftFacing;


	private void Start()
	{
		m_character = GetComponentInParent<KinematicCharacter>(); // TODO: handle attachment/detachment

		GetComponent<Cloth>().capsuleColliders = m_character.GetComponentsInChildren<CapsuleCollider>(); // TODO: also support sphere collider pairs? don't stomp existing capsuleColliders[] entries?
	}

	private void LateUpdate()
	{
		gameObject.transform.localPosition = (Vector3)(m_offsetFromHead ? m_character.HeadOffset : m_character.ArmOffset) + (m_character.LeftFacing ? new(-m_offset.x, m_offset.y, m_offset.z) : m_offset);
		if (m_wasLeftFacing != m_character.LeftFacing)
		{
			gameObject.transform.localRotation = m_character.LeftFacing ? Quaternion.Euler(0.0f, 180.0f, 0.0f) : Quaternion.identity;
			m_wasLeftFacing = m_character.LeftFacing;
		}
	}
}
