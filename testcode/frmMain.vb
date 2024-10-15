Imports System.Windows.Forms

Public Class frmMain

	Private Sub btnPreview_Click(sender As Object, e As EventArgs) Handles btnPreview.Click
		DocumentPreviewControl1.ShowPreview(txtPreviewFile.Text)
	End Sub

	''' <summary>
	''' TODO
	''' </summary>
	''' <param name="sender"></param>
	''' <param name="e"></param>
	Private Sub btnHidePreview_Click(sender As Object, e As EventArgs) Handles btnHidePreview.Click
		DocumentPreviewControl1.HidePreview()
	End Sub

	Private Sub btnFile_Click(sender As Object, e As EventArgs) Handles btnFile.Click
		Dim dialog As OpenFileDialog = New OpenFileDialog()
		dialog.Filter = "All files (*.*)|*.*"
		dialog.FileName = txtPreviewFile.Text

		If (dialog.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK) Then
			txtPreviewFile.Text = dialog.FileName
		End If
	End Sub

End Class