using UnityEngine;


public class LineConnector : MonoBehaviour
{
	public Vector3 m_lineOffset;
	public float m_lengthMin = 0.05f;


	private LineRenderer[] m_lines;

	private ArmController m_arm;
	private AnchoredJoint2D[] m_joints;


	private void Start()
	{
		m_lines = GetComponentsInChildren<LineRenderer>();
		m_arm = GetComponent<ArmController>();
		m_joints = GetComponents<AnchoredJoint2D>();
	}

	private void Update()
	{
		AvatarController avatar = transform.parent.GetComponent<AvatarController>();
		EnemyController enemy = transform.parent.GetComponent<EnemyController>();
		Vector3 parentOffset = avatar != null ? avatar.m_armOffset : enemy != null ? enemy.m_armOffset : Vector3.zero; // TODO: unify {Avatar/Enemy}Controller.m_armOffset?
		Vector3 shoulderPosLocal = transform.parent.position + parentOffset + (m_arm == null ? Vector3.zero : (Vector3)(Vector2)m_arm.m_offset) - transform.position; // NOTE the removal of Z from m_arm.m_offset

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
			}

			++i;
		}
	}
}
