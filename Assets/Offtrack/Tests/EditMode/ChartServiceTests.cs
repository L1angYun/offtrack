using System;
using System.Globalization;
using NUnit.Framework;
using Offtrack.Runtime;

namespace Offtrack.Tests.EditMode
{
    public sealed class ChartServiceTests
    {
        [TestCase("[{}]")]
        [TestCase("[{\"t\":1}]")]
        [TestCase("[{\"lane\":1}]")]
        [TestCase("[{\"time\":1,\"lane\":1}]")]
        [TestCase("[{\"t\":\"1\",\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":\"1\"}]")]
        [TestCase("[{\"t\":null,\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":null}]")]
        [TestCase("[{\"t\":true,\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":false}]")]
        [TestCase("[{\"t\":{},\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":[1]}]")]
        [TestCase("[{\"t\":1,\"lane\":1.5}]")]
        [TestCase("[{\"t\":1,\"t\":2,\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":1,\"lane\":2}]")]
        public void MissingAmbiguousOrWronglyTypedFieldsCannotBecomePlayableNotes(string json)
        {
            Assert.Throws<ArgumentException>(() => ChartService.Parse(json, 3));
        }

        [TestCase("[{\"t\":01,\"lane\":1}]")]
        [TestCase("[{\"t\":+1,\"lane\":1}]")]
        [TestCase("[{\"t\":1e,\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":1},]")]
        [TestCase("[{\"t\":1,\"lane\":1}][]")]
        [TestCase("[{\"t\":1e999,\"lane\":1}]")]
        [TestCase("[{\"t\":1,\"lane\":2147483648}]")]
        public void MalformedJsonAndOverflowAreRejected(string json)
        {
            Assert.Throws<ArgumentException>(() => ChartService.Parse(json, 3));
        }

        [Test]
        public void ZeroTimesAndExponentNotationWorkInEitherFieldOrderIndependentOfCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var notes = ChartService.Parse(" [ {\"lane\":0, \"t\":0},\n{\"t\":1.25e-1,\"lane\":2}, {\"lane\":1,\"t\":2E+1} ] ", 3);
                Assert.That(notes, Has.Length.EqualTo(3));
                Assert.That(notes[0].Time, Is.Zero);
                Assert.That(notes[0].Lane, Is.Zero);
                Assert.That(notes[1].Time, Is.EqualTo(.125));
                Assert.That(notes[1].Lane, Is.EqualTo(2));
                Assert.That(notes[2].Time, Is.EqualTo(20));
                Assert.That(notes[2].Lane, Is.EqualTo(1));
                Assert.That(ChartService.Parse(ChartService.ToJson(notes), 3), Is.EqualTo(notes));
                Assert.That(ChartService.Parse("[ ]", 3), Is.Empty);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
