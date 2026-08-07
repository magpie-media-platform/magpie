using Magpie.Extensions.Cardigann.Engine;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann.Engine;

[TestFixture]
public sealed class CardigannLoginBlockParsingTests
{
    private const string BaseYaml = """
        id: priv
        name: Priv
        type: private
        links:
          - https://tracker.test/
        search:
          paths:
            - path: /search
          rows:
            selector: tr.row
          fields:
            title:
              selector: td.title
        """;

    [Test]
    public void LoadString_ParsesFormLoginGraph_WhenFullLoginBlockPresent()
    {
        // Arrange
        var yaml = BaseYaml +
            """

            login:
              path: /login.php
              submitpath: /takelogin.php
              method: form
              form: form#login
              inputs:
                username: "{{ .Config.username }}"
                password: "{{ .Config.password }}"
                keeplogged: 1
              selectorinputs:
                csrf:
                  selector: input[name="csrf"]
                  attribute: value
              getselectorinputs:
                token:
                  selector: input[name="token"]
                  attribute: value
              error:
                - selector: div.error
                  message:
                    selector: div.error
              test:
                path: /
                selector: a.logout
            """;
        var loader = new CardigannLoader();

        // Act
        var login = loader.LoadString(yaml, "priv").Login;

        // Assert
        Assert.That(login, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(login!.Method, Is.EqualTo("form"));
            Assert.That(login.Path, Is.EqualTo("/login.php"));
            Assert.That(login.SubmitPath, Is.EqualTo("/takelogin.php"));
            Assert.That(login.Form, Is.EqualTo("form#login"));
            Assert.That(login.Inputs, Does.ContainKey("username"));
            Assert.That(login.Inputs!["password"], Is.EqualTo("{{ .Config.password }}"));
            Assert.That(login.SelectorInputs, Does.ContainKey("csrf"));
            Assert.That(login.SelectorInputs!["csrf"].Attribute, Is.EqualTo("value"));
            Assert.That(login.GetSelectorInputs, Does.ContainKey("token"));
            Assert.That(login.Error, Has.Count.EqualTo(1));
            Assert.That(login.Error![0].Selector, Is.EqualTo("div.error"));
            Assert.That(login.Error[0].Message!.Selector, Is.EqualTo("div.error"));
            Assert.That(login.Test!.Selector, Is.EqualTo("a.logout"));
            Assert.That(login.Captcha, Is.Null);
        }
    }

    [Test]
    public void LoadString_ParsesCookieAndCaptchaBlocks_WhenPresent()
    {
        // Arrange
        var yaml = BaseYaml +
            """

            login:
              method: cookie
              inputs:
                cookie: "{{ .Config.cookie }}"
              captcha:
                type: image
                selector: img#captcha
                input: captcha_answer
              test:
                path: /
                selector: a.logout
            """;
        var loader = new CardigannLoader();

        // Act
        var login = loader.LoadString(yaml, "priv").Login;

        // Assert
        Assert.That(login, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(login!.Method, Is.EqualTo("cookie"));
            Assert.That(login.Inputs!["cookie"], Is.EqualTo("{{ .Config.cookie }}"));
            Assert.That(login.Captcha, Is.Not.Null);
        }
    }
}
