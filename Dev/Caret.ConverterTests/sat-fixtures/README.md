# SAT test fixtures

`cfdi40-valid.xml` comes from the tests of [eclipxe13/CfdiUtils](https://github.com/eclipxe13/CfdiUtils) (`tests/assets/cfdi40-valid.xml`, MIT licence). It is sealed with the SAT's public **test** certificate (CSD 30001000000500003416, test company ESCUELA KEMPER URGATE, RFC EKU9003173C9). It contains no real taxpayer's invoice. `CfdiSealTests.cs` checks its seal with the SAT's own stylesheets for the cadena original.
