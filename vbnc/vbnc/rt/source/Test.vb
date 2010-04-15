' 
' Visual Basic.Net Compiler
' Copyright (C) 2004 - 2008 Rolf Bjarne Kvinge, RKvinge@novell.com
' 
' This library is free software; you can redistribute it and/or
' modify it under the terms of the GNU Lesser General Public
' License as published by the Free Software Foundation; either
' version 2.1 of the License, or (at your option) any later version.
' 
' This library is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY; without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
' Lesser General Public License for more details.
' 
' You should have received a copy of the GNU Lesser General Public
' License along with this library; if not, write to the Free Software
' Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
' 

<Serializable()> _
Public Class Test
    Private Const PEVerifyPath As String = "%programfiles%\Microsoft Visual Studio 8\SDK\v2.0\Bin\PEVerify.exe"
    Private Const PEVerifyPath2 As String = "%programfiles%\Microsoft SDKs\Windows\v6.0A\bin\PEVerify.exe"

    ''' <summary>
    ''' The id of the test
    ''' </summary>
    ''' <remarks></remarks>
    Private m_ID As String

    ''' <summary>
    ''' The name of the test
    ''' </summary>
    ''' <remarks></remarks>
    Private m_Name As String

    Private m_Category As String
    Private m_Priority As Integer
    Private m_Arguments As String
    Private m_KnownFailure As String

    ''' <summary>
    ''' The target (winexe, exe, library, module)
    ''' </summary>
    ''' <remarks></remarks>
    Private m_Target As Targets

    ''' <summary>
    ''' The files that contains this test.
    ''' </summary>
    Private m_Files As New Specialized.StringCollection

    ''' <summary>
    ''' The exit code the compiler should return
    ''' </summary>
    ''' <remarks></remarks>
    Private m_ExpectedExitCode As Integer

    ''' <summary>
    ''' The error (or warning) the compiler should return (0 for none).
    ''' </summary>
    ''' <remarks></remarks>
    Private m_ExpectedErrorCode As Integer

    ''' <summary>
    ''' The result of the test
    ''' </summary>
    ''' <remarks></remarks>
    Private m_Result As Results

    ''' <summary>
    ''' The test container
    ''' </summary>
    ''' <remarks></remarks>
    Private m_Parent As Tests

    ''' <summary>
    ''' How long did the test take?
    ''' </summary>
    ''' <remarks></remarks>
    Private m_TestDuration As TimeSpan

    Private m_Verifications As New Generic.List(Of VerificationBase)

    Private m_WorkingDirectory As String

    ''' <summary>
    ''' The compilation using our compiler.
    ''' </summary>
    ''' <remarks></remarks>
    Private m_Compilation As VerificationBase

    Private m_LastRun As Date

    Private m_Tag As Object
    Private m_DontExecute As Boolean

    Public Event Executed(ByVal Sender As Test)
    Public Event Executing(ByVal Sender As Test)
    Public Event Changed(ByVal Sender As Test)

    Private m_Compiler As String
    Private Shared m_NegativeRegExpTest As New System.Text.RegularExpressions.Regex("^\d\d\d\d.*$", System.Text.RegularExpressions.RegexOptions.Compiled)
    Private Shared m_FileCache As New Collections.Generic.Dictionary(Of String, String())
    Private Shared m_FileCacheTime As Date = Date.MinValue

    Public Property ID() As String
        Get
            Return m_ID
        End Get
        Set(ByVal value As String)
            If Not String.IsNullOrEmpty(m_ID) Then
                Throw New ArgumentException("This test already has an ID")
            End If

            If String.IsNullOrEmpty(value) Then
                Throw New ArgumentException("Invalid ID")
            End If

            m_ID = value
        End Set
    End Property

    Private Function GetAttributeValue(ByVal attrib As XmlAttribute) As String
        If attrib Is Nothing Then Return Nothing
        Return attrib.Value
    End Function

    Private Function GetNodeValue(ByVal node As XmlNode) As String
        If node Is Nothing Then Return Nothing
        Return node.InnerText
    End Function

    Public Sub SetResult(ByVal result As String)
        If result Is Nothing Then result = String.Empty
        Select Case result.ToLower()
            Case Nothing, "", "notrun"
                m_Result = Results.NotRun
            Case Else
                m_Result = CType([Enum].Parse(GetType(Results), result, True), Results)
        End Select
    End Sub

    Public Sub LoadResult(ByVal xml As XmlNode)
        Dim result As String = GetAttributeValue(xml.Attributes("result"))
        For Each vb As XmlNode In xml.SelectNodes("Verification")
            Dim type As String = GetAttributeValue(vb.Attributes("Type"))
            Dim name As String = GetAttributeValue(vb.Attributes("Name"))
            Dim executable As String
            Dim expandablecommandline As String
            Dim verification As VerificationBase
            Select Case type
                Case GetType(ExternalProcessVerification).FullName
                    Dim extvb As ExternalProcessVerification
                    Dim process As XmlNode = vb.SelectSingleNode("Process")
                    executable = GetAttributeValue(process.Attributes("Executable"))
                    expandablecommandline = GetAttributeValue(process.Attributes("UnexpandedCommandLine"))
                    extvb = New ExternalProcessVerification(Me, executable, expandablecommandline)
                    extvb.Process.StdOut = process.SelectSingleNode("StdOut").Value
                    verification = extvb
                Case GetType(CecilCompare).FullName
                    Dim cecilvb As CecilCompare
                    cecilvb = New CecilCompare(Me)
                    verification = cecilvb
                Case Else
                    Throw New NotImplementedException(type)
            End Select
            verification.DescriptiveMessage = GetAttributeValue(vb.Attributes("DescriptiveMessage"))
            verification.Name = name
            verification.Result = CBool(GetAttributeValue(vb.Attributes("Result")))
            verification.Run = CBool(GetAttributeValue(vb.Attributes("Run")))
            Me.m_Verifications.Add(verification)
        Next
        SetResult(result)
    End Sub

    Public Sub Load(ByVal xml As XmlNode)
        Dim target As String

        m_ID = xml.Attributes("id").Value
        m_Name = xml.Attributes("name").Value
        m_Category = GetAttributeValue(xml.Attributes("category"))
        m_Priority = Integer.Parse(xml.Attributes("priority").Value)
        m_Arguments = GetNodeValue(xml.SelectSingleNode("arguments"))
        m_KnownFailure = GetAttributeValue(xml.Attributes("knownfailure"))

        m_ExpectedErrorCode = CInt(GetAttributeValue(xml.Attributes("expectederrorcode")))
        m_ExpectedExitCode = CInt(GetAttributeValue(xml.Attributes("expectedexitcode")))
        m_WorkingDirectory = GetAttributeValue(xml.Attributes("workingdirectory"))

        SetResult(GetAttributeValue(xml.Attributes("result")))


        '    'Test to see if it is a negative test.
        '    'Negative tests are:
        '    '0001.vb
        '    '0001-2.vb
        '    '0001-3 sometest.vb
        If m_NegativeRegExpTest.IsMatch(m_Name) Then
            Dim firstNonNumber As Integer = m_Name.Length
            For i As Integer = 0 To m_Name.Length - 1
                If Char.IsNumber(m_Name(i)) = False Then
                    firstNonNumber = i
                    Exit For
                End If
            Next
            If Integer.TryParse(m_Name.Substring(0, firstNonNumber), m_ExpectedErrorCode) Then
                If m_ExpectedErrorCode >= 40000 AndAlso m_ExpectedErrorCode < 50000 Then
                    m_ExpectedExitCode = 0
                Else
                    m_ExpectedExitCode = 1
                End If
            End If
        End If

        target = GetAttributeValue(xml.Attributes("target"))
        If target IsNot Nothing Then target = target.ToLower()
        Select Case GetAttributeValue(xml.Attributes("target"))
            Case "exe"
                m_Target = Targets.Exe
            Case "winexe"
                m_Target = Targets.Winexe
            Case "library"
                m_Target = Targets.Library
            Case "module"
                m_Target = Targets.Module
            Case "", Nothing, "none"
                'Console.WriteLine("Warning: {0} does not have a target specified.", m_ID)
                m_Target = Targets.None
            Case Else
                Throw New InvalidOperationException("Invalid target: " & target)
        End Select
        For Each file As XmlNode In xml.SelectNodes("file")
            m_Files.Add(file.InnerText)
        Next

        'This is just temporary
        If m_Arguments.Contains("/target:library") Then
            m_Target = Targets.Library
            m_Arguments = m_Arguments.Replace("/target:library", "")
        End If

    End Sub

    Public Sub Save(ByVal xml As Xml.XmlWriter, ByVal results As Boolean)
        xml.WriteStartElement("test")
        xml.WriteAttributeString("id", m_ID)
        If results = False Then
            xml.WriteAttributeString("name", m_Name)
            xml.WriteAttributeString("category", m_Category)
            xml.WriteAttributeString("priority", m_Priority.ToString())
            If Not String.IsNullOrEmpty(m_KnownFailure) Then
                xml.WriteAttributeString("knownfailure", m_KnownFailure)
            End If

            If m_ExpectedExitCode <> 0 Then xml.WriteAttributeString("expectedexitcode", m_ExpectedExitCode.ToString())
            If m_ExpectedErrorCode <> 0 Then xml.WriteAttributeString("expectederrorcode", m_ExpectedErrorCode.ToString())

            xml.WriteAttributeString("target", m_Target.ToString().ToLower())
            If Not String.IsNullOrEmpty(m_WorkingDirectory) Then xml.WriteAttributeString("workingdirectory", m_WorkingDirectory)
            xml.WriteElementString("arguments", m_Arguments)
            For Each file As String In m_Files
                xml.WriteElementString("file", file)
            Next
        Else
            xml.WriteAttributeString("result", m_Result.ToString().ToLower())
            For Each vb As VerificationBase In Me.Verifications
                xml.WriteStartElement("Verification")
                xml.WriteAttributeString("Type", vb.GetType().FullName)
                xml.WriteAttributeString("Name", vb.Name)
                xml.WriteAttributeString("DescriptiveMessage", vb.DescriptiveMessage)
                xml.WriteAttributeString("ExpectedErrorCode", vb.ExpectedErrorCode.ToString())
                xml.WriteAttributeString("ExpectedExitCode", vb.ExpectedExitCode.ToString())
                xml.WriteAttributeString("Result", vb.Result.ToString())
                xml.WriteAttributeString("Run", vb.Run.ToString())

                Dim extvb As ExternalProcessVerification = TryCast(vb, ExternalProcessVerification)
                If extvb IsNot Nothing Then
                    If extvb.Process IsNot Nothing Then
                        xml.WriteStartElement("Process")
                        xml.WriteAttributeString("Executable", extvb.Process.Executable)
                        xml.WriteAttributeString("UnexpandedCommandLine", extvb.Process.UnexpandedCommandLine)
                        xml.WriteElementString("StdOut", extvb.Process.StdOut)
                        xml.WriteEndElement()
                    End If
                End If

                xml.WriteEndElement()
            Next
        End If
        xml.WriteEndElement()
    End Sub

    Property ExpectedExitCode() As Integer
        Get
            Return m_ExpectedExitCode
        End Get
        Set(ByVal value As Integer)
            m_ExpectedExitCode = value
        End Set
    End Property

    Property ExpectedErrorCode() As Integer
        Get
            Return m_ExpectedErrorCode
        End Get
        Set(ByVal value As Integer)
            m_ExpectedErrorCode = value
        End Set
    End Property


    Public Property KnownFailure() As String
        Get
            Return m_KnownFailure
        End Get
        Set(ByVal value As String)
            m_KnownFailure = value
        End Set
    End Property

    ReadOnly Property IsKnownFailure() As Boolean
        Get
            Return String.IsNullOrEmpty(m_KnownFailure) = False
        End Get
    End Property

    Property Compiler() As String
        Get
            Return m_Compiler
        End Get
        Set(ByVal value As String)
            m_Compiler = value
        End Set
    End Property

    Function GetOldResults() As Generic.List(Of OldResult)
        Dim result As New Generic.List(Of OldResult)
        Dim allfiles() As String

        If IO.Directory.Exists(Me.OutputPath) = False Then
            IO.Directory.CreateDirectory(Me.OutputPath)
        End If

        If m_FileCache.ContainsKey(Me.OutputPath) = False OrElse (Date.Now - m_FileCacheTime).TotalMinutes > 1 Then
            allfiles = IO.Directory.GetFiles(Me.OutputPath, "*.testresult")
            m_FileCache(Me.OutputPath) = allfiles
            m_FileCacheTime = Date.Now
        Else
            allfiles = m_FileCache(Me.OutputPath)
        End If

        Dim files As New Generic.List(Of String)
        Try
            Dim start As String = IO.Path.DirectorySeparatorChar & Me.Name & ".("
            For i As Integer = 0 To allfiles.Length - 1
                If allfiles(i).Contains(start) Then
                    files.Add(allfiles(i))
                End If
            Next
        Catch io As IO.IOException

        End Try

        For Each file As String In files
            result.Add(New OldResult(file))
        Next
        Return result
    End Function

    Property Tag() As Object
        Get
            Return m_Tag
        End Get
        Set(ByVal value As Object)
            m_Tag = value
        End Set
    End Property

    Public Property Category() As String
        Get
            Return m_Category
        End Get
        Set(ByVal value As String)
            m_Category = value
        End Set
    End Property

    ReadOnly Property LastRun() As Date
        Get
            Return m_LastRun
        End Get
    End Property

    ReadOnly Property TestDuration() As TimeSpan
        Get
            Return m_TestDuration
        End Get
    End Property

    ReadOnly Property Verifications() As VerificationBase()
        Get
            Return m_Verifications.ToArray
        End Get
    End Property

    ''' <summary>
    ''' The test container
    ''' </summary>
    ''' <value></value>
    ''' <remarks></remarks>
    Friend ReadOnly Property Parent() As Tests
        Get
            Return m_Parent
        End Get
    End Property

    ''' <summary>
    ''' Has this test been run?
    ''' </summary>
    ''' <value></value>
    ''' <remarks></remarks>
    ReadOnly Property Run() As Boolean
        Get
            Return m_Result > Results.NotRun
        End Get
    End Property

    ''' <summary>
    ''' The path of where the output files are
    ''' </summary>
    ''' <value></value>
    ''' <remarks></remarks>
    ReadOnly Property OutputPath() As String
        Get
            Return IO.Path.Combine(FullWorkingDirectory, "testoutput")
        End Get
    End Property

    ''' <summary>
    ''' The files that contains this test.
    ''' </summary>
    <System.ComponentModel.Editor("System.Windows.Forms.Design.StringCollectionEditor, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", GetType(System.Drawing.Design.UITypeEditor))> _
    ReadOnly Property Files() As Specialized.StringCollection
        Get
            Return m_Files
        End Get
    End Property

    ''' <summary>
    ''' The StdOut of the test
    ''' </summary>
    ''' <remarks></remarks>
    ReadOnly Property StdOut() As String
        Get
            Dim external As ExternalProcessVerification = TryCast(m_Compilation, ExternalProcessVerification)
            If external IsNot Nothing Then
                If external.Process IsNot Nothing Then Return external.Process.StdOut
            End If

            Return String.Empty
        End Get
    End Property

    ''' <summary>
    ''' The exit code of the compilation
    ''' </summary>
    ''' <remarks></remarks>
    ReadOnly Property ExitCode() As Integer
        Get
            Dim external As ExternalProcessVerification = TryCast(m_Compilation, ExternalProcessVerification)

            If external IsNot Nothing Then
                If external.Process Is Nothing Then Return 0
                Return external.Process.ExitCode
            Else
                Return 0
            End If
        End Get
    End Property

    ''' <summary>
    ''' The name of the test.
    ''' </summary>
    ''' <value></value>
    ''' <remarks></remarks>
    Public Property Name() As String
        Get
            Return m_Name
        End Get
        Set(ByVal value As String)
            m_Name = value
        End Set
    End Property

    ''' <summary>
    ''' The result of the test.
    ''' </summary>
    ''' <value></value>
    ''' <remarks></remarks>
    ReadOnly Property Success() As Boolean
        Get
            Return m_Result = Results.Success
        End Get
    End Property

    ReadOnly Property Skipped() As Boolean
        Get
            Return m_Result = Results.Skipped
        End Get
    End Property

    ''' <summary>
    ''' THe result of the test.
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    ReadOnly Property Result() As Results
        Get
            Return m_Result
        End Get
    End Property

    ''' <summary>
    ''' Has this test multiple files?
    ''' </summary>
    ''' <value></value>
    ''' <remarks></remarks>
    ReadOnly Property IsMultiFile() As Boolean
        Get
            Return m_Files.Count > 1
        End Get
    End Property

    Property Target() As Targets
        Get
            Return m_Target
        End Get
        Set(ByVal value As Targets)
            m_Target = value
        End Set
    End Property

    ReadOnly Property TargetExtension() As String
        Get
            Select Case m_Target
                Case Targets.Exe, Targets.Winexe
                    Return "exe"
                Case Targets.Module
                    Return "netmodule"
                Case Targets.Library
                    Return "dll"
                Case Else
                    Throw New InvalidOperationException("Invalid target: " & m_Target.ToString())
            End Select
        End Get
    End Property

    Function GetOutputAssembly() As String
        Return IO.Path.Combine(Me.OutputPath, Name & "." & TargetExtension)
    End Function

    Function GetOutputVBCAssembly() As String
        Return IO.Path.Combine(Me.OutputPath, Name & "_vbc." & TargetExtension)
    End Function

    ''' <summary>
    ''' Get the xml output files.
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Function GetOutputFiles() As String()
        Dim result As String()
        If IO.Directory.Exists(OutputPath) Then
            result = New String() {} 'IO.Directory.GetFiles(OutputPath, Name & OutputPattern)
        Else
            result = New String() {}
        End If
        Return result
    End Function

    Function GetVerifiedFiles() As String()
        Dim result As String()
        If IO.Directory.Exists(OutputPath) Then
            result = New String() {} 'IO.Directory.GetFiles(OutputPath, Name & VerifiedPattern)
        Else
            result = New String() {}
        End If
        Return result
    End Function

    ''' <summary>
    ''' Returns the commandline arguments to execute this test. Does not include the compiler executable.
    ''' Arguments are not quoted!
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Function GetTestCommandLineArguments(Optional ByVal ForVBC As Boolean = False) As String()
        Dim result As New Generic.List(Of String)

        'Initialize()

        'First option is always the /out: argument.
        Const OutArgument As String = "-out:{0}"
        Dim outputFilename, outputPath As String
        If ForVBC Then
            outputFilename = GetOutputVBCAssembly()
            If outputFilename Is Nothing Then Return New String() {}
        Else
            outputFilename = GetOutputAssembly()
        End If
        outputPath = IO.Path.GetDirectoryName(outputFilename)
        If outputPath <> "" AndAlso IO.Directory.Exists(outputPath) = False Then
            IO.Directory.CreateDirectory(outputPath)
        End If
        result.Add(String.Format(OutArgument, outputFilename))

        If m_Arguments IsNot Nothing Then
            result.AddRange(m_Arguments.Split(New Char() {" "c, Chr(10), Chr(13)}, StringSplitOptions.RemoveEmptyEntries))
        End If

        For Each file As String In Files
            result.Add(file)
        Next

        Select Case m_Target
            Case Targets.Library
                result.Add("/target:library")
            Case Targets.Exe
                result.Add("/target:exe")
            Case Targets.Winexe
                result.Add("/target:winexe")
            Case Targets.Module
                result.Add("/target:module")
        End Select

        Return result.ToArray()
    End Function

    ReadOnly Property FailedVerification() As VerificationBase
        Get
            If m_Verifications Is Nothing Then Return Nothing
            For Each v As VerificationBase In m_Verifications
                If v.Result = False Then Return v
            Next
            Return Nothing
        End Get
    End Property

    ReadOnly Property FailedVerificationMessage() As String
        Get
            Dim tmp As VerificationBase = FailedVerification
            If tmp IsNot Nothing Then Return tmp.DescriptiveMessage
            Return ""
        End Get
    End Property

    'Sub Initialize()
    '    Dim rsp As String

    '    rsp = IO.Path.Combine(m_BasePath, Name) & ".response"
    '    If IO.File.Exists(rsp) Then m_ResponseFile = rsp Else m_ResponseFile = ""
    '    rsp = IO.Path.Combine(m_BasePath, Name) & ".rsp"
    '    If IO.File.Exists(rsp) Then m_RspFile = rsp Else m_RspFile = ""
    '    rsp = IO.Path.Combine(m_BasePath, "all.rsp")
    '    If IO.File.Exists(rsp) Then m_DefaultRspFile = rsp Else m_DefaultRspFile = ""

    '    'Find the target of the test (exe, winexe, library, module)
    '    m_Target = "exe" 'default target.
    '    If m_RspFile <> "" Then
    '        ParseResponseFile(m_RspFile)
    '    Else
    '        If m_DefaultRspFile <> "" Then
    '            ParseResponseFile(m_DefaultRspFile)
    '        End If
    '        If m_ResponseFile <> "" Then
    '            ParseResponseFile(m_ResponseFile)
    '        End If
    '    End If
    '    m_TargetExtension = GetTargetExtension(m_Target)
    'End Sub

    Function GetExecutor() As String
        Return IO.Path.GetFullPath("..\..\rt-execute\rt-execute.exe".Replace("\", IO.Path.DirectorySeparatorChar))
    End Function

    Property Arguments() As String
        Get
            Return m_Arguments
        End Get
        Set(ByVal value As String)
            m_Arguments = value
        End Set
    End Property

    Property WorkingDirectory() As String
        Get
            Return m_WorkingDirectory
        End Get
        Set(ByVal value As String)
            m_WorkingDirectory = value
        End Set
    End Property

    ReadOnly Property FullWorkingDirectory() As String
        Get
            If String.IsNullOrEmpty(m_WorkingDirectory) Then
                Return IO.Path.GetDirectoryName(m_Parent.Filename)
            Else
                Return IO.Path.Combine(IO.Path.GetDirectoryName(m_Parent.Filename), m_WorkingDirectory)
            End If
        End Get
    End Property

    ''' <summary>
    ''' Returns true if new verifications have been created (only if source files has changed
    ''' or vbnc compiler has changed since last run).
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Function CreateVerifications() As Boolean
        Dim vbnccmdline As String() = Helper.QuoteStrings(Me.GetTestCommandLineArguments(False))
        Dim vbccmdline As String() = Helper.QuoteStrings(Me.GetTestCommandLineArguments(True))

        Dim vbc As ExternalProcessVerification = Nothing
        Dim compiler As String = Nothing
        Dim vbccompiler As String = Nothing

        If Me.Parent IsNot Nothing Then
            vbccompiler = Me.Parent.VBCPath
        End If

        If vbccompiler <> String.Empty AndAlso vbccmdline.Length > 0 Then
            vbc = New ExternalProcessVerification(Me, vbccompiler, Join(vbccmdline, " "))
            vbc.Process.WorkingDirectory = FullWorkingDirectory
            vbc = vbc
        End If

        If vbc IsNot Nothing Then
            vbc.Name = "VBC Compile (verifies that the test itself is correct)"
            vbc.ExpectedExitCode = m_ExpectedExitCode
            vbc.ExpectedErrorCode = m_ExpectedErrorCode
        End If

        compiler = Me.Compiler
        If compiler Is Nothing AndAlso Me.Parent IsNot Nothing Then
            compiler = Me.Parent.VBNCPath
        End If
        If compiler Is Nothing Then
            Throw New Exception("No compiler specified.")
        End If

        Dim external_compilation As ExternalProcessVerification = Nothing

        external_compilation = New ExternalProcessVerification(Me, compiler, Join(vbnccmdline, " "))
        external_compilation.Process.WorkingDirectory = FullWorkingDirectory
        m_Compilation = external_compilation

        m_Compilation.Name = "VBNC Compile"
        'm_Compilation.Process.UseTemporaryExecutable = True
        m_Compilation.ExpectedErrorCode = m_ExpectedErrorCode
        m_Compilation.ExpectedExitCode = m_ExpectedExitCode

        m_Verifications.Clear()

        If vbc IsNot Nothing Then m_Verifications.Add(vbc)
        m_Verifications.Add(m_Compilation)

        If m_ExpectedExitCode = 0 Then
            If vbccompiler <> String.Empty AndAlso Me.m_Target = Targets.Exe AndAlso m_DontExecute = False AndAlso Me.GetOutputVBCAssembly IsNot Nothing Then
                m_Verifications.Add(New ExternalProcessVerification(Me, Me.GetOutputVBCAssembly))
                m_Verifications(m_Verifications.Count - 1).Name = "Test executable verification"
            End If

            Dim peverify As String
            peverify = Environment.ExpandEnvironmentVariables(PEVerifyPath)
            If peverify = String.Empty OrElse IO.File.Exists(peverify) = False Then peverify = Environment.ExpandEnvironmentVariables(PEVerifyPath2)
            If peverify <> String.Empty AndAlso IO.File.Exists(peverify) Then
                Dim peV As New ExternalProcessVerification(Me, peverify, "%OUTPUTASSEMBLY% /nologo /verbose")
                peV.Name = "Type Safety and Security Verification"
                peV.Process.WorkingDirectory = Me.OutputPath
                m_Verifications.Add(peV)
            End If

            Dim cc As CecilCompare
            If vbccompiler <> String.Empty AndAlso IO.File.Exists(vbccompiler) AndAlso GetOutputVBCAssembly() IsNot Nothing Then
                cc = New CecilCompare(Me)
                cc.Name = "Cecil Assembly Compare"
                m_Verifications.Add(cc)
            End If

            If Me.m_Target = Targets.Exe AndAlso m_DontExecute = False Then
                Dim executor As String
                executor = GetExecutor()
                If executor <> String.Empty AndAlso IO.File.Exists(executor) Then
                    m_Verifications.Add(New ExternalProcessVerification(Me, executor))
                Else
                    m_Verifications.Add(New ExternalProcessVerification(Me, Me.GetOutputAssembly))
                End If
                m_Verifications(m_Verifications.Count - 1).Name = "Output executable verification"
            End If
        End If

        m_Result = Results.NotRun

        Return True
    End Function

    Sub SaveTest()
        'Const DATETIMEFORMAT As String = "yyyy-MM-dd HHmm"
        'Dim compiler As String = ""
        'Dim filename As String

        'Try
        '    Dim vbnc As ExternalProcessVerification = DirectCast(VBNCVerification, ExternalProcessVerification)
        '    If vbnc IsNot Nothing Then
        '        compiler = "(" & vbnc.Process.FileVersion.FileVersion & " " & vbnc.Process.LastWriteDate.ToString(DATETIMEFORMAT) & ")"
        '    End If

        '    compiler &= "." & m_Result.ToString

        '    Dim i As Integer
        '    i = compiler.IndexOfAny(IO.Path.GetInvalidPathChars)
        '    If i >= 0 Then
        '        For Each c As Char In IO.Path.GetInvalidPathChars
        '            compiler = compiler.Replace(c.ToString(), "")
        '        Next
        '    End If

        '    filename = IO.Path.Combine(Me.OutputPath, Me.Name & "." & compiler & ".testresult")
        '    Using contents As New Xml.XmlTextWriter(filename, Nothing)
        '        contents.Formatting = Xml.Formatting.Indented
        '        If False Then
        '            Dim ser As New Xml.Serialization.XmlSerializer(GetType(Test))
        '            ser.Serialize(contents, Me)
        '        Else
        '            contents.WriteStartDocument(True)
        '            contents.WriteStartElement("Test")
        '            contents.WriteElementString("Name", Me.Name)
        '            contents.WriteStartElement("Date")
        '            contents.WriteValue(Me.LastRun)
        '            contents.WriteEndElement()
        '            contents.WriteElementString("Compiler", compiler)
        '            contents.WriteElementString("Result", Me.Result.ToString)
        '            contents.WriteElementString("IsNegativeTest", Me.IsNegativeTest.ToString)
        '            contents.WriteElementString("NegativeError", Me.NegativeError.ToString)
        '            contents.WriteElementString("TestDuration", Me.TestDuration.ToString)

        '            contents.WriteStartElement("Verifications")
        '            For Each ver As VerificationBase In Me.Verifications
        '                contents.WriteStartElement(ver.GetType.Name)
        '                contents.WriteElementString("Name", ver.Name)
        '                contents.WriteElementString("Result", ver.Result.ToString)
        '                contents.WriteElementString("Run", ver.Run.ToString)
        '                contents.WriteElementString("NegativeError", ver.NegativeError.ToString)
        '                contents.WriteElementString("DescriptiveMessage", ver.DescriptiveMessage)
        '                contents.WriteEndElement()
        '            Next
        '            contents.WriteEndElement()

        '            contents.WriteEndElement()
        '            contents.WriteEndDocument()
        '        End If
        '    End Using
        'Catch ex As Exception
        '    Console.WriteLine(ex.Message & vbNewLine & ex.StackTrace)
        'End Try
    End Sub

    Function SkipTest() As Boolean
        If Helper.IsOnWindows Then
            Return Name.EndsWith(".Linux", StringComparison.OrdinalIgnoreCase)
        Else
            Return Name.EndsWith(".Windows", StringComparison.OrdinalIgnoreCase)
        End If
    End Function

    Sub DoTest()
        'If BasePath <> "" Then
        '    Environment.CurrentDirectory = BasePath
        'End If
        If CreateVerifications() = False Then
            Return
        End If

        m_Result = Results.Running
        RaiseEvent Executing(Me)

        Dim StartTime, EndTime As Date
        StartTime = Date.Now
        If SkipTest() Then
            m_Result = Results.Skipped
        Else
            For i As Integer = 0 To m_Verifications.Count - 1
                Dim v As VerificationBase = m_Verifications(i)
                If v.Verify = False Then
                    m_Result = Results.Failed
                    Exit For
                End If
            Next
        End If
        EndTime = Date.Now
        m_TestDuration = EndTime - StartTime
        m_LastRun = StartTime

        If m_Result = Results.Running Then
            m_Result = Results.Success
        End If
        If IsKnownFailure Then
            If m_Result = Results.Success Then
                m_Result = Results.KnownFailureSucceeded
            ElseIf m_Result = Results.Failed Then
                m_Result = Results.KnownFailureFailed
            Else
                m_Result = Results.KnownFailureFailed
            End If
        End If

        SaveTest()

        RaiseEvent Executed(Me)
    End Sub

    ReadOnly Property VBNCVerification() As VerificationBase
        Get
            For Each ver As VerificationBase In m_Verifications
                If ver.Name.Contains("VBNC Compile") Then
                    Return ver
                End If
            Next
            Return Nothing
        End Get
    End Property

    ReadOnly Property Message() As String
        Get
            Dim result As String = ""
            For Each v As VerificationBase In m_Verifications
                If v IsNot Nothing Then
                    result &= v.DescriptiveMessage & vbNewLine & New String("*"c, 50) & vbNewLine
                End If
            Next
            Return result
        End Get
    End Property

    Shared Function GetTestName(ByVal Filename As String) As String
        Dim result As String
        result = IO.Path.GetFileNameWithoutExtension(Filename)
        If Filename Like "*.[0-9].vb" Then 'Multi file test.
            result = IO.Path.GetFileNameWithoutExtension(result)
        End If
        Return result
    End Function

    Sub New(ByVal Parent As Tests)
        m_Parent = Parent
    End Sub

    Sub New(ByVal Parent As Tests, ByVal xml As XmlNode)
        m_Parent = Parent
        Load(xml)
    End Sub

    'Sub New(ByVal Path As String, ByVal Parent As Tests)
    '    m_Parent = Parent
    '    If Path.EndsWith(IO.Path.DirectorySeparatorChar) Then
    '        Path = Path.Remove(Path.Length - 1, 1)
    '    End If

    '    m_BasePath = IO.Path.GetDirectoryName(Path)
    '    m_Files.Add(Path)

    '    m_Name = GetTestName(Path)

    '    'Test to see if it is a negative test.
    '    'Negative tests are:
    '    '0001.vb
    '    '0001-2.vb
    '    '0001-3 sometest.vb
    '    If m_NegativeRegExpTest.IsMatch(m_Name) Then
    '        Dim firstNonNumber As Integer = m_Name.Length
    '        For i As Integer = 0 To m_Name.Length - 1
    '            If Char.IsNumber(m_Name(i)) = False Then
    '                firstNonNumber = i
    '                Exit For
    '            End If
    '        Next
    '        m_IsNegativeTest = Integer.TryParse(m_Name.Substring(0, firstNonNumber), m_NegativeError)
    '        If m_IsNegativeTest AndAlso m_NegativeError >= 40000 AndAlso m_NegativeError < 50000 Then
    '            m_IsNegativeTest = False
    '            m_IsWarning = True
    '        End If
    '    End If
    '    m_OutputPath = IO.Path.Combine(m_BasePath, DefaultOutputPath)
    '    Initialize()
    'End Sub

    Private Function IsNoConfig(ByVal text As String) As Boolean
        Return text.IndexOf("/noconfig", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Function GetTarget(ByVal text As String, ByVal DefaultTarget As String) As String
        Dim prefixes As String() = New String() {"/target:", "/t:", "-target:", "-t:"}
        For Each prefix As String In prefixes
            If text.IndexOf(prefix & "exe", StringComparison.OrdinalIgnoreCase) >= 0 Then Return "exe"
            If text.IndexOf(prefix & "winexe", StringComparison.OrdinalIgnoreCase) >= 0 Then Return "winexe"
            If text.IndexOf(prefix & "library", StringComparison.OrdinalIgnoreCase) >= 0 Then Return "dll"
            If text.IndexOf(prefix & "module", StringComparison.OrdinalIgnoreCase) >= 0 Then Return "netmodule"
        Next
        Return DefaultTarget
    End Function

    Private Function GetTargetExtension(ByVal Target As String) As String
        Select Case Target
            Case "winexe", "exe"
                Return "exe"
            Case "library", "dll"
                Return "dll"
            Case "netmodule", "module"
                Return "netmodule"
            Case Else
                Return "exe"
        End Select
    End Function
End Class