using UnityEngine;


public class DisableAnimatorControl : StateMachineBehaviour
{
	private AvatarController m_avatar;
	private AIController m_ai;

	private bool m_wasPassive;


	// OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
	override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
	{
		if (animator.TryGetComponent(out m_avatar))
		{
			m_avatar.DisablePlayerControl();
		}
		if (animator.TryGetComponent(out m_ai))
		{
			m_wasPassive = m_ai.m_passive;
			m_ai.m_passive = true;
		}
	}

	// OnStateExit is called when a transition ends and the state machine finishes evaluating this state
	override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
	{
		if (m_avatar != null)
		{
			m_avatar.EnablePlayerControl();
		}
		if (m_ai != null)
		{
			m_ai.m_passive = m_wasPassive;
		}
	}
}
