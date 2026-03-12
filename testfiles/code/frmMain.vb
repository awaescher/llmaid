Imports System.Windows.Forms

Public Class frmMain
	Inherits Form

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

		If Not Me.Visible And UpdateTitleAfter5Pm() Then
			Me.Show()
		End If

		If Me.Visible And Not Me.Enabled Then
			Me.Hide()
		End If

		If Me.Visible = False Then
			Me.Enabled = False
		Else
			Me.Enabled = True
		End If

	End Sub

	Private Sub btnFile_Click(sender As Object, e As EventArgs) Handles btnFile.Click
		Dim dialog As OpenFileDialog = New OpenFileDialog()
		dialog.Filter = "All files (*.*)|*.*"
		dialog.FileName = txtPreviewFile.Text

		If dialog.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK And Not Me.Disposed Then
			txtPreviewFile.Text = dialog.FileName
		End If
	End Sub

	Public Function UpdateTitleAfter5Pm() As Boolean

		If (DateTime.Now.Hour >= 17) Then
			Me.Text = "After 5 PM"
			Return True
		End If

		Return False
	End Function

End Class