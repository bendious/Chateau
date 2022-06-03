using System.Linq;
using UnityEngine;


[DisallowMultipleComponent]
public class LineConnector : MonoBehaviour
{
	public Vector3 m_lineOffset;
	public float m_lengthMin = 0.05f;


	private LineRenderer[] m_lines;

	private ArmController m_arm;
	private AnchoredJoint2D[] m_joints;

	private AvatarController m_avatar;
	private EnemyController m_enemy;
	private SpriteRenderer m_parentRenderer;


	private void Start()
	{
		m_lines = GetComponentsInChildren<LineRenderer>();
		m_arm = GetComponent<ArmController>();
		m_joints = GetComponents<AnchoredJoint2D>();
		m_avatar = transform.parent.GetComponent<AvatarController>();
		m_enemy = transform.parent.GetComponent<EnemyController>();
		m_parentRenderer = m_avatar != null ? m_avatar.GetComponent<SpriteRenderer>() : m_enemy != null ? m_enemy.GetComponent<SpriteRenderer>() : null;
	}

	private void LateUpdate()
	{
		Vector3 parentOffset = m_avatar != null ? m_avatar.m_armOffset : m_enemy != null ? m_enemy.m_armOffset : Vector3.zero; // TODO: unify {Avatar/Enemy}Controller.m_armOffset?
		Vector3 shoulderPosLocal = transform.parent.position + parentOffset + (m_arm == null ? Vector3.zero : (Vector3)(Vector2)m_arm.m_offset) - transform.position; // NOTE the removal of Z from m_arm.m_offset
		float alpha = m_parentRenderer == null ? 1.0f : m_parentRenderer.color.a;
		System.Lazy<GradientAlphaKey[]> newAlphaKeys = new(() => new GradientAlphaKey[] { new GradientAlphaKey(alpha, 0.0f) }, false);

		int i = 0;
		foreach (LineRenderer line in m_lines)
		{
			// calculate start/end
			// TODO: simplify?
			Vector3 offsetOriented = m_arm != null && m_arm.LeftFacing ? -m_lineOffset : m_lineOffset;
			Quaternion rotInv = Quaternion.Inverse(transform.rotation);
			AnchoredJoint2D joint = m_joints.Length > i ? m_joints[i] : null;
			Vector3 startPosLocal = offsetOriented + (joint == null ? Vector3.zero : (Vector3)joint.anchor);
			Vector3 endPosLocal = rotInv * (joint == null ? shoulderPosLocal : (joint.connectedBody == null ? (Vector3)joint.connectedAnchor : joint.connectedBody.transform.position + joint.connectedBody.transform.rotation * joint.connectedAnchor) - transform.position) + offsetOriented;

			// update renderer
			line.enabled = ((Vector2)endPosLocal - (Vector2)startPosLocal).sqrMagnitude >= m_lengthMin * m_lengthMin;
			if (line.enabled)
			{
				line.SetPosition(0, startPosLocal);
				line.SetPosition(1, endPosLocal);
				if (line.colorGradient.alphaKeys.First().alpha != alpha) // TODO: don't assume a constant alpha across the line?
				{
					line.colorGradient = new() { colorKeys = line.colorGradient.colorKeys, alphaKeys = newAlphaKeys.Value }; // NOTE that we have to replace the whole gradient rather than just setting individual attributes due to the annoying way LineRenderer prevents those changes
				}
			}

			++i;
		}
	}
}
