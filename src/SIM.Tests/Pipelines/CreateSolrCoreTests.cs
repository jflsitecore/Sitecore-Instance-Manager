﻿using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using SIM.Instances;
using SIM.Pipelines.Install.Modules;
using SIM.Products;

namespace SIM.Tests.Pipelines
{
  [TestClass]
  public class CreateSolrCoreTests
  {

    #region Constants

    private const string SolrBasePath = @"c:\some\path";
    private const string DllPath = @"c:\some\website\bin\Sitecore.ContentSearch.dll";
    private const string SchemaPath = @"c:\some\path\SOME_CORE_NAME\conf\schema.xml";
    private const string SolrCorePath = @"c:\some\path\SOME_CORE_NAME";
    private const string SolrConfigPath = @"c:\some\path\SOME_CORE_NAME\conf\solrconfig.xml";
    private const string ManagedSchemaPath = @"c:\some\path\SOME_CORE_NAME\conf\managed-schema";
    private const string TemplateCollectionPath = @"c:\some\path\collection1";
    #endregion

    #region Fields

    private CreateSolrCores _sut;
    private Instance _instance;
    private Product _module;
    private Stream _infoResponse;
 


    #endregion

    #region Setup and helper methods

    [TestInitialize]
    public void SetUp()
    {
      _sut = Substitute.For<CreateSolrCores>();
      _instance = Substitute.For<Instance>();
      _instance.WebRootPath.Returns(@"c:\some\website\");
      _module = Substitute.For<Product>();
      XmlDocument doc = new XmlDocument();
      doc.LoadXml(GetConfigXml("SOME_URL", "SOME_CORE_NAME", "SOME_ID"));
      _instance.GetShowconfig().Returns(doc);

    }

    [TestCleanup]
    public void CleanUp()
    {
      _infoResponse.Dispose();
    }

    private void Arrange()
    {
      ArrangeGetSolrInfo();
      _sut.FileExists(TemplateCollectionPath).Returns(true);
      _sut.FileExists(SchemaPath).Returns(true);
      _sut.FileExists(ManagedSchemaPath).Returns(false);
      var xmlDocumentEx = XmlDocumentEx.LoadXml("<anonymous />");
      _sut.XmlMerge(Arg.Any<string>(), Arg.Any<string>()).Returns(xmlDocumentEx);

    }


    private void Act()
    {
      _sut.Execute(_instance, _module);
    }

    private void ArrangeGetSolrInfo()
    {
      string response =
        @"<response><lst name=""lucene""><str name=""solr-spec-version"">4.0.0</str></lst><str name=""solr_home"">c:\some\path</str></response>";
      _infoResponse = GenerateStreamFromString(response);
 
      _sut.RequestAndGetResponseStream("SOME_URL/admin/info/system").Returns(_infoResponse);
      
    }

    private static Stream GenerateStreamFromString(string s)
    {
      MemoryStream stream = new MemoryStream();
      StreamWriter writer = new StreamWriter(stream);
      writer.Write(s);
      writer.Flush();
      stream.Position = 0;
      return stream;
    }

    private string GetConfigXml(string someUrl, string someCoreName, string someId)
    {
      return "<sitecore>" +
             "<settings>" +
             $"<setting name='ContentSearch.Solr.ServiceBaseAddress' value='{someUrl}' />" +
             "</settings>" +
             "<contentSearch>" +
             "<configuration>" +
             "<indexes>" +
             $"<index  type='Sitecore.ContentSearch.SolrProvider.SolrSearchIndex, Sitecore.ContentSearch.SolrProvider' id='{someId}'>" +
             $"<param desc='core' id='$(id)'>{someCoreName}</param>" +
             "</index></indexes></configuration></contentSearch></sitecore>";
    }

    #endregion

    #region Tests

    [TestMethod]
    public void ShouldGetSystemInfo()
    {
      Arrange();

      Act();

      _sut.Received().RequestAndGetResponseStream("SOME_URL/admin/info/system");
    }

    [TestMethod]
    public void ShouldCopyStockConfigIfSolr5()
    {
      Arrange();
      _infoResponse = GenerateStreamFromString(
        "<response>" +
        "<lst name=\"lucene\">" +
        "<str name=\"solr-spec-version\">5.0.0</str>" +
        "</lst>" +
        $"<str name=\"solr_home\">{SolrBasePath}</str>" +
        "</response>");

      _sut.RequestAndGetResponseStream("SOME_URL/admin/info/system").Returns(_infoResponse);
      _sut.FileExists(Arg.Any<string>()).Returns(true); //TODO tighten

      Act();

      _sut.Received().CopyDirectory(SolrBasePath + @"\configsets\data_driven_schema_configs", SolrCorePath);
      _sut.Received().XmlMerge($"{SolrBasePath}\\SOME_CORE_NAME\\conf\\solrconfig.xml", Arg.Any<string>());


    }

    [TestMethod]
    public void ShouldCopyCollection1InstanceDirToNewCorePath()
    {
      Arrange();

      Act();

      _sut.Received().CopyDirectory(@"c:\some\path\collection1", @"c:\some\path\SOME_CORE_NAME");
    }

    [TestMethod]
    public void ShouldDeletePropertiesFile()
    {
      Arrange();

      Act();

      _sut.Received().DeleteFile(@"c:\some\path\SOME_CORE_NAME\core.properties");
    }

    [TestMethod]
    public void ShouldCallSolrCreateCore()
    {
      Arrange();

      _sut.Execute(_instance,_module);

      var dirPath = @"c:\some\path\SOME_CORE_NAME";
      string coreName = "SOME_CORE_NAME";
      _sut.Received()
        .RequestAndGetResponseStream(
          $"SOME_URL/admin/cores?action=CREATE&name={coreName}&instanceDir={dirPath}&config=solrconfig.xml&schema=schema.xml&dataDir=data");

    }

    [TestMethod]
    public void ShouldCallGenerateAssembly()
    {
      Arrange();
 
      Act();

      _sut.Received().InvokeSitecoreGenerateSchemaUtility(DllPath, SchemaPath, SchemaPath);
    }

    [TestMethod]
    public void ShouldUseManagedSchemaFileWhenNoSchema()
    {
      Arrange();
      _sut.FileExists(SchemaPath).Returns(false);
      _sut.FileExists(ManagedSchemaPath).Returns(true);

      Act();

      _sut.Received().InvokeSitecoreGenerateSchemaUtility(DllPath, ManagedSchemaPath, SchemaPath);
    }

    [TestMethod, ExpectedException(typeof(FileNotFoundException))]
    public void ShouldThrowIfNoSchema()
    {
      Arrange();
      _sut.FileExists(SchemaPath).Returns(false);
      _sut.FileExists(ManagedSchemaPath).Returns(false);

      Act();
    }

    /// <summary>
    /// See https://github.com/dsolovay/sitecore-instance-manager/issues/38
    /// </summary>
    [TestMethod]
    public void ShouldMergeTermConfigSettings()
    {
      Arrange();

      Act();

      _sut.Received().XmlMerge(SolrConfigPath, CreateSolrCores.SolrTermSuppportPatch);
    }

    [TestMethod]
    public void ShouldRemoveUpdateProcessor()
    {
      Arrange();
      string solrconfig =
        @"<config>
            <updateRequestProcessorChain>
                <processor class=""solr.AddSchemaFieldsUpdateProcessorFactory"" />
            </updateRequestProcessorChain>
          </config>";

      var xmlDocumentEx = XmlDocumentEx.LoadXml(solrconfig);
      _sut.XmlMerge(Arg.Any<string>(), Arg.Any<string>()).Returns(xmlDocumentEx);

      Act();

      _sut.Received().WriteAllText(Arg.Any<string>(),
        Arg.Is<string>(s => !s.Contains(@"solr.AddSchemaFieldsUpdateProcessorFactory")));
    }

    [TestMethod]
    public void ShouldChangeManagedToClassicConfig()
    {
      Arrange();
      string solrconfig =
        @"<config>
            <schemaFactory class=""ManagedIndexSchemaFactory"">
              <bool name=""mutable"">true</bool>
              <str name=""managedSchemaResourceName"" >managed-schema</str>
            </schemaFactory >
          </config>";
        
      var xmlDocumentEx = XmlDocumentEx.LoadXml(solrconfig);
      _sut.XmlMerge(Arg.Any<string>(), Arg.Any<string>()).Returns(xmlDocumentEx);

      Act();

      _sut.Received().WriteAllText(Arg.Any<string>(),
        Arg.Is<string>(s => s.Contains(@"<schemaFactory class=""ClassicIndexSchemaFactory"" />")
                            && !s.Contains("ManagedIndexSchemaFactory")));
    }
 
    [TestMethod]
    public void ShouldSaveToSolrConfigPath()
    {
      Arrange();
      
      Act();

      _sut.Received().WriteAllText(
        path: SolrConfigPath,
        text: Arg.Any<string>());
    }

    [TestMethod]
    public void ShouldNormalizeSolrConfig()
    {
      Arrange();
      const string nonIndentedXml = "<a><b/></a>";
      var xmlDocumentEx = XmlDocumentEx.LoadXml(nonIndentedXml);
      _sut.XmlMerge(Arg.Any<string>(), Arg.Any<string>()).Returns(xmlDocumentEx);

      Act();

      string xmlDocumentDeclaration_WithUtf8 = @"<?xml version=""1.0"" encoding=""UTF-8"" ?>";

      // Note: StartsWith, Contains, EndsWith used to avoid polluting test with
      // <schemaFactory> element.
      _sut.Received().WriteAllText(
        path: Arg.Any<string>(),
        text: Arg.Is<string>(s =>
          s.StartsWith(xmlDocumentDeclaration_WithUtf8 + "\r\n<a>\r\n") && 
          s.Contains("\r\n  <b />\r\n") &&
          s.EndsWith("\r\n</a>")));
    }

    #endregion

  }
}
