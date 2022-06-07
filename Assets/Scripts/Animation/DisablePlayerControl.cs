using UnityEngine;


public class DisablePlayerControl : StateMachineBehaviour
{
	private AvatarController m_avatar;


	// OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
	override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
	{
		m_avatar = animator.GetComponent<AvatarController>();
		if (m_avatar != null)
		{
			m_avatar.DeactivateAllControl();
		}
	}

	// OnStateExit is called when a transition ends and the state machine finishes evaluating this state
	override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
	{
		if (m_avatar != null)
		{
			m_avatar.EnablePlayerControl();
		}
	}
}
