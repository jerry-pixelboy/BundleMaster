using System;
namespace UnityEditor.Extension
{
    /// <summary>
    /// �б�ѡ��
    /// </summary>
	internal enum ListViewOptions
	{
        /// <summary>
        /// �϶���������
        /// </summary>
		wantsReordering = 1,
        /// <summary>
        /// �����ⲿ�ļ��Ϸ�
        /// </summary>
		wantsExternalFiles,
        /// <summary>
        /// �Զ�����ҷ
        /// </summary>
		wantsToStartCustomDrag = 4,
        /// <summary>
        /// �����Զ�����ҷ
        /// </summary>
		wantsToAcceptCustomDrag = 8
	}
}
