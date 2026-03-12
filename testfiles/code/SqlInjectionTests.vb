Imports System.Windows.Forms

Public Class SqlInjectionTests

    Public Function CreateEmail(subject As String, body As String) As String
        Dim email = "Subject: " & subject & vbCrLf & "Body: " & body
        Return email
    End Function

    Public Function GenerateFilePath(directory As String, fileName As String) As String
        Return directory & "\" & fileName
    End Function

    Public Function BuildUrl(base As String, endpoint As String) As String
        Return base & "/" & endpoint
    End Function

    Public Function GenerateHtmlLink(url As String, text As String) As String
        Return "<a href='" & url & "'>" & text & "</a>"
    End Function

    Public Function CreateJsonString(key As String, value As String) As String
        Dim jsonString = "{" & """" & key & """" & ": " & """" & value & """" & "}"
        Return jsonString
    End Function

	Public Function CountByIdString(id As String) As Int32
		Dim sql = "SELECT COUNT(*) FROM THINGS WHERE IDSTRING = '" & id & "'"
		Return Database.Execute(sql)
	End Function

	Public Function CountActiveByIdString(id As String) As Int32
		Dim sql = "SELECT COUNT(*) FROM THINGS WHERE IDSTRING = '" & id & "' AND ISACTIVE = 1"
		Return Database.Execute(sql)
	End Function

	Public Function CountByIdStringFormatted(id As String) As Int32
		Dim sql = String.Format("SELECT COUNT(*) FROM THINGS WHERE IDSTRING = '{0}'", id)
		Return Database.Execute(sql)
	End Function

	Public Function CountByIdStringWithMakeSqlValue(id As String) As Int32
		Dim sql = "SELECT COUNT(*) FROM THINGS WHERE IDSTRING = " & MakeSqlValue(id)
		Return Database.Execute(sql)
	End Function

	Public Function CountByIntId(id As Int32) As Int32
		Dim sql = "SELECT COUNT(*) FROM THINGS WHERE ID = " & id.ToString()
		Return Database.Execute(sql)
	End Function

	Public Function CountActiveByIntId(id As Int32) As Int32
		Dim sql = "SELECT COUNT(*) FROM THINGS WHERE ID = " & id.ToString() & " AND ISACTIVE = 1"
		Return Database.Execute(sql)
	End Function

	Public Function CountById(id As String) As Int32
		Dim sql = "SELECT COUNT(*) FROM THINGS WHERE ID = " & MakeSqlValue(id)
		Return Database.Execute(sql)
	End Function

	Public Function WriteLog(user As String, entityType As String, id As String) As Int32
		Dim message = "User " & user & " changed " & entityType & " with Id '" & id & "' at " & DateTime.Now.ToString()
		Return WriteLog(message)
	End Function

End Class